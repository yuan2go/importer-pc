#!/usr/bin/env python3
"""Independent-clock, loopback-only TCP-camera/RCS audit fixture.

Python 3.10+ standard library. This is synthetic protocol evidence, never camera,
vendor SDK, production RCS or Windows soak evidence. It does not edit app config.
"""
import argparse
import asyncio
import collections
import datetime as dt
import hashlib
import json
import math
import pathlib
import time


def utc():
    return dt.datetime.now(dt.timezone.utc).isoformat()


class Fixture:
    def __init__(self, args):
        self.args = args
        self.output = pathlib.Path(args.output).resolve()
        self.output.mkdir(parents=True, exist_ok=False)
        self.logfile = (self.output / "events.jsonl").open("x", encoding="utf-8")
        self.started = time.monotonic()
        self.started_utc = utc()
        self.counts = collections.Counter()
        self.clients = {}
        self.connections = set()
        self.rcs_connections = 0
        self.rcs_requests = 0
        self.destination_requests = 0
        self.handlers = set()
        self.evidence_bytes = 0
        self.quota_hit = False
        self.stop = asyncio.Event()
        self.last_flush = self.started
        self.last_sequence = {"left": 0, "right": 0}
        self.log("run_started", scope="SYNTHETIC_LOOPBACK_ONLY", arguments=vars(args))

    def log(self, kind, **fields):
        self.counts[kind] += 1
        line = json.dumps({"utc": utc(), "elapsed_s": time.monotonic() - self.started,
                          "event": kind, **fields}, ensure_ascii=False) + "\n"
        if self.quota_hit:
            return
        if self.evidence_bytes + len(line.encode()) > self.args.max_evidence_bytes:
            self.quota_hit = True
            self.logfile.write(json.dumps({"utc": utc(), "event": "evidence_quota_exceeded",
                                          "status": "INCOMPLETE_EVIDENCE_ABORTED"}) + "\n")
            self.logfile.flush()
            self.stop.set()
            return
        self.evidence_bytes += len(line.encode())
        self.logfile.write(line)
        if time.monotonic() - self.last_flush >= 1:
            self.logfile.flush()
            self.last_flush = time.monotonic()

    async def camera(self, position, reader, writer):
        handler = asyncio.current_task()
        self.handlers.add(handler)
        self.connections.add(writer)
        if position in self.clients:
            self.log("camera_extra_client_rejected", position=position)
            writer.close()
            self.connections.discard(writer)
            self.handlers.discard(handler)
            return
        self.clients[position] = writer
        self.log("camera_connected", position=position, peer=writer.get_extra_info("peername"))
        try:
            # The audited client sends no application bytes; unexpected traffic is recorded.
            while data := await reader.read(1024):
                self.log("unexpected_client_bytes", position=position, length=len(data))
        except (ConnectionError, OSError) as error:
            self.log("camera_socket_error", position=position, error=type(error).__name__)
        finally:
            if self.clients.get(position) is writer:
                self.clients.pop(position, None)
            writer.close()
            self.connections.discard(writer)
            self.handlers.discard(handler)
            self.log("camera_disconnected", position=position)

    async def generate(self, position, rate):
        if rate == 0:
            self.log("camera_idle_load_selected", position=position)
            return
        period = 1.0 / rate
        sequence = 0
        # Start scheduling only after a visible lead-in, independently of app responses.
        due = self.started + self.args.warmup
        end = self.started + self.args.duration
        while due < end and not self.stop.is_set():
            await asyncio.sleep(max(0.0, due - time.monotonic()))
            if self.stop.is_set():
                break
            sequence += 1
            self.last_sequence[position] = sequence
            barcode = "AUDIT-" + position[0].upper() + "-" + str(sequence).zfill(10)
            lag = max(0.0, time.monotonic() - due)
            self.log("input_scheduled", position=position, sequence=sequence, barcode=barcode,
                     scheduled_elapsed_s=due-self.started, scheduler_lag_s=lag)
            writer = self.clients.get(position)
            if not writer or writer.is_closing():
                self.log("input_not_written", position=position, sequence=sequence, reason="no_connected_client")
            elif self.args.disconnect_every and sequence % self.args.disconnect_every == 0:
                self.log("injected_camera_disconnect", position=position, sequence=sequence)
                writer.close()
            elif writer.transport.get_write_buffer_size() > 65536:
                self.log("input_not_written", position=position, sequence=sequence, reason="bounded_socket_backlog")
                writer.close()
            else:
                frame = (barcode + "\n").encode("utf-8")
                if self.args.invalid_every and sequence % self.args.invalid_every == 0:
                    frame = b"\xff\n"
                if self.args.oversize_every and sequence % self.args.oversize_every == 0:
                    frame = b"X" * 4097 + b"\n"
                try:
                    writer.write(frame)
                    self.log("input_written_to_socket_buffer", position=position, sequence=sequence,
                             barcode=barcode, bytes=len(frame), frame_sha256=hashlib.sha256(frame).hexdigest())
                except (ConnectionError, OSError, RuntimeError) as error:
                    self.log("input_not_written", position=position, sequence=sequence, reason=type(error).__name__)
            due += period
            # Avoid an unbounded catch-up flood if the generator itself is stalled.
            # Every missed slot remains an explicit generator failure, never a passed input.
            behind = int(max(0.0, time.monotonic() - due) / period)
            if behind > 100:
                self.log("generator_slots_missed", position=position, first_sequence=sequence+1,
                         last_sequence=sequence+behind, missed=behind)
                sequence += behind
                due += behind * period

    async def rcs(self, reader, writer):
        handler = asyncio.current_task()
        self.handlers.add(handler)
        self.connections.add(writer)
        if self.rcs_connections >= 32:
            self.log("rcs_connection_rejected", reason="fixture_connection_limit")
            writer.close()
            self.connections.discard(writer)
            self.handlers.discard(handler)
            return
        self.rcs_connections += 1
        try:
            while not self.stop.is_set():
                try:
                    head = await asyncio.wait_for(reader.readuntil(b"\r\n\r\n"), 15)
                except asyncio.IncompleteReadError:
                    return
                if len(head) > 16384:
                    raise ValueError("request_headers_too_large")
                lines = head.decode("ascii").split("\r\n")
                if lines[0] != "POST /rcs/sorter/api HTTP/1.1":
                    raise ValueError("unsupported_request_line")
                headers = {}
                for line in lines[1:]:
                    if line:
                        key, value = line.split(":", 1)
                        key = key.lower()
                        if key in headers:
                            raise ValueError("duplicate_http_header")
                        headers[key] = value.strip()
                if "transfer-encoding" in headers:
                    raise ValueError("chunked_request_not_supported_by_fixture")
                length = int(headers["content-length"])
                if length < 0 or length > 65536:
                    raise ValueError("request_body_quota")
                raw = await asyncio.wait_for(reader.readexactly(length), 10)
                request = json.loads(raw)
                self.rcs_requests += 1
                ordinal = self.rcs_requests
                request_id = request["requestId"]
                data = request["data"]
                commands = [item["command"] for item in data]
                self.log("rcs_request_received", ordinal=ordinal, request_id=request_id,
                         commands=commands, request=request, body_sha256=hashlib.sha256(raw).hexdigest())
                is_destination = commands == ["sorter.task.dest_request"]
                if is_destination:
                    self.destination_requests += 1
                destination_ordinal = self.destination_requests
                if is_destination and self.args.drop_every and destination_ordinal % self.args.drop_every == 0:
                    self.log("injected_rcs_drop_after_receive", ordinal=ordinal, request_id=request_id)
                    return
                if is_destination and self.args.rcs_delay_ms:
                    await asyncio.sleep(self.args.rcs_delay_ms / 1000)
                response_id = request_id
                if is_destination and self.args.mismatch_every and destination_ordinal % self.args.mismatch_every == 0:
                    response_id += 1
                results = []
                for item in data:
                    if item["command"] == "sorter.task.dest_request":
                        params = item["params"]
                        position = "left" if str(params["loadCode"]).startswith("AUDIT-L-") else "right"
                        result = {"chuteCode": "L07" if position == "left" else "R09",
                                  "taskId": "AUDIT-TASK-" + str(request_id), "skuId": "AUDIT-SKU",
                                  "waveNo": "AUDIT-WAVE", "quantity": str(params["quantity"]),
                                  "length": None, "width": None, "height": None, "weight": None}
                    elif item["command"] == "sorter.task.station_heartbeat":
                        result = {}
                    else:
                        raise ValueError("unsupported_rcs_command")
                    results.append({"index": item["index"], "command": item["command"], "success": True,
                                    "code": None, "message": None, "args": None, "data": result})
                body = json.dumps({"requestId": response_id, "result": results}, separators=(",", ":")).encode()
                http_status = 503 if is_destination and self.args.fail_every and destination_ordinal % self.args.fail_every == 0 else 200
                reply = (f"HTTP/1.1 {http_status} {'OK' if http_status == 200 else 'Service Unavailable'}\r\n"
                         f"Content-Type: application/json\r\nContent-Length: {len(body)}\r\nConnection: keep-alive\r\n\r\n").encode() + body
                writer.write(reply)
                await asyncio.wait_for(writer.drain(), 5)
                self.log("rcs_response_written", ordinal=ordinal, request_id=request_id,
                         response_id=response_id, status=http_status, response_sha256=hashlib.sha256(body).hexdigest(),
                         application_received="UNCONFIRMED")
        except (ConnectionError, OSError, ValueError, KeyError, TypeError, asyncio.TimeoutError,
                asyncio.IncompleteReadError, asyncio.LimitOverrunError) as error:
            self.log("rcs_fixture_connection_ended", error=type(error).__name__, reason=str(error)[:256])
        finally:
            self.rcs_connections -= 1
            writer.close()
            self.connections.discard(writer)
            self.handlers.discard(handler)

    async def run(self):
        servers = []
        tasks = []
        interrupted = False
        failure = None
        try:
            for position, port in (("left", self.args.left_port), ("right", self.args.right_port)):
                server = await asyncio.start_server(lambda r, w, p=position: self.camera(p, r, w), "127.0.0.1", port)
                servers.append(server)
            servers.append(await asyncio.start_server(self.rcs, "127.0.0.1", self.args.rcs_port, limit=16385))
            self.log("listeners_ready", address="127.0.0.1")
            print(json.dumps({"status": "RUNNING_SYNTHETIC_FIXTURE", "output": str(self.output),
                              "rcs": f"http://127.0.0.1:{self.args.rcs_port}/rcs/sorter/api",
                              "left_port": self.args.left_port, "right_port": self.args.right_port}), flush=True)
            tasks = [asyncio.create_task(self.generate("left", self.args.left_hz)),
                     asyncio.create_task(self.generate("right", self.args.right_hz))]
            watcher = asyncio.create_task(self.stop.wait())
            try:
                done, pending = await asyncio.wait([*tasks, watcher], timeout=self.args.duration,
                                                   return_when=asyncio.FIRST_EXCEPTION)
                for task in done:
                    if task.exception() is not None:
                        raise task.exception()
                interrupted = self.stop.is_set()
            finally:
                watcher.cancel()
                await asyncio.gather(watcher, return_exceptions=True)
        except asyncio.CancelledError:
            interrupted = True
        except Exception as error:
            failure = type(error).__name__ + ": " + str(error)[:256]
            self.log("fixture_failed", error=failure)
            raise
        finally:
            self.stop.set()
            for task in tasks:
                task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)
            for server in servers:
                server.close()
                await server.wait_closed()
            for writer in list(self.connections):
                writer.close()
            handlers = list(self.handlers)
            for handler in handlers:
                handler.cancel()
            await asyncio.gather(*handlers, return_exceptions=True)
            self.log("run_stopped", interrupted=interrupted)
            self.logfile.flush()
            summary = {"started_utc": self.started_utc, "ended_utc": utc(), "actual_duration_seconds": time.monotonic()-self.started,
                       "requested_duration_seconds": self.args.duration, "interrupted": interrupted,
                       "fixture_failure": failure,
                       "evidence_quota_exceeded": self.quota_hit,
                       "counts": dict(self.counts), "scope": "SYNTHETIC_TCP_AND_HTTP_FIXTURE_ONLY",
                       "windows_wpf": "NOT_EVALUATED", "native_sdk": "NOT_RUN", "real_equipment": "NOT_RUN",
                       "app_business_reconciliation": "REQUIRED_SEPARATELY", "soak_pass": "NOT_DETERMINED"}
            (self.output / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
            print(json.dumps(summary), flush=True)
            self.logfile.close()


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, help="New isolated evidence directory (must not exist)")
    parser.add_argument("--duration", type=float, default=60, help="Seconds; 86400 = 24 hours, actual duration is recorded")
    parser.add_argument("--warmup", type=float, default=10, help="Lead-in before independent-clock sends")
    parser.add_argument("--left-hz", type=float, default=1)
    parser.add_argument("--right-hz", type=float, default=1)
    parser.add_argument("--left-port", type=int, default=19001)
    parser.add_argument("--right-port", type=int, default=19002)
    parser.add_argument("--rcs-port", type=int, default=19080)
    parser.add_argument("--rcs-delay-ms", type=int, default=0)
    parser.add_argument("--max-evidence-bytes", type=int, default=1073741824,
                        help="Bound evidence at 1 GiB by default; reaching it aborts with incomplete evidence")
    for name in ("drop-every", "fail-every", "mismatch-every", "disconnect-every", "invalid-every", "oversize-every"):
        parser.add_argument("--" + name, type=int, default=0, help="Fault experiment only; 0 disables")
    args = parser.parse_args()
    if not 0 < args.duration <= 604800 or not 0 <= args.warmup < args.duration:
        parser.error("Require 0 <= warmup < duration <= 604800")
    if any(not math.isfinite(rate) or not 0 <= rate <= 100 for rate in (args.left_hz, args.right_hz)):
        parser.error("Each rate must be finite and 0 <= Hz <= 100 (zero means idle)")
    if args.max_evidence_bytes < 1048576:
        parser.error("Evidence quota must be at least 1 MiB")
    ports = (args.left_port, args.right_port, args.rcs_port)
    if len(set(ports)) != 3 or any(not 1024 <= port <= 65535 for port in ports):
        parser.error("Use three distinct ports in 1024..65535")
    if not 0 <= args.rcs_delay_ms <= 60000 or any(getattr(args, key.replace("-", "_")) < 0 for key in
        ("drop-every", "fail-every", "mismatch-every", "disconnect-every", "invalid-every", "oversize-every")):
        parser.error("Fault values must be nonnegative; RCS delay <= 60000 ms")
    return args


if __name__ == "__main__":
    try:
        fixture = Fixture(arguments())
        asyncio.run(fixture.run())
    except KeyboardInterrupt:
        pass
