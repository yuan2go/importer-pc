"""Local smoke of the audit fixture itself, not the application or SDK."""
import asyncio
import datetime
import json
import pathlib
import sys
import time


async def main():
    root = pathlib.Path(__file__).resolve().parent
    tag = str(time.time_ns())
    command = [sys.executable, str(root / "loopback_load.py"), "--output", str(root / ("fixture-smoke-" + tag)),
               "--duration", "3", "--warmup", "0.8", "--left-hz", "4", "--right-hz", "4",
               "--left-port", "19801", "--right-port", "19802", "--rcs-port", "19880"]
    start = datetime.datetime.now(datetime.timezone.utc).isoformat()
    clock = time.monotonic()
    process = await asyncio.create_subprocess_exec(*command, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE)
    ready = await asyncio.wait_for(process.stdout.readline(), 5)
    error = None
    writers = []
    try:
        assert json.loads(ready).get("status") == "RUNNING_SYNTHETIC_FIXTURE", ready.decode()
        left, left_writer = await asyncio.open_connection("127.0.0.1", 19801)
        right, right_writer = await asyncio.open_connection("127.0.0.1", 19802)
        writers.extend([left_writer, right_writer])
        left_line, right_line = await asyncio.wait_for(asyncio.gather(left.readline(), right.readline()), 3)
        assert left_line.startswith(b"AUDIT-L-") and right_line.startswith(b"AUDIT-R-")
        reader, writer = await asyncio.open_connection("127.0.0.1", 19880)
        writers.append(writer)
        body = json.dumps({"requestId": 1000000000001, "data": [{"index": 1, "command": "sorter.task.dest_request",
                          "params": {"loadCode": left_line.decode().strip(), "quantity": 1}}]}).encode()
        writer.write(b"POST /rcs/sorter/api HTTP/1.1\r\nContent-Type: application/json\r\nContent-Length: " +
                     str(len(body)).encode() + b"\r\n\r\n" + body)
        await writer.drain()
        headers = await asyncio.wait_for(reader.readuntil(b"\r\n\r\n"), 2)
        length = int(next(line.split(b":", 1)[1] for line in headers.split(b"\r\n") if line.lower().startswith(b"content-length:")))
        response = json.loads(await reader.readexactly(length))
        assert response["requestId"] == 1000000000001 and response["result"][0]["data"]["chuteCode"] == "L07"
    except Exception as caught:
        error = repr(caught)
    finally:
        for writer in writers:
            writer.close()
        rest, stderr = await asyncio.wait_for(process.communicate(), 8)
    (root / "fixture-stdout.log").write_bytes(ready + rest)
    (root / "fixture-stderr.log").write_bytes(stderr)
    record = {"command": command, "startedUtc": start, "endedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "durationSeconds": time.monotonic() - clock, "exitCode": process.returncode, "smokeError": error,
              "checks": ["left_barcode_identity", "right_barcode_identity", "rcs_request_id_echo", "left_chute_response"],
              "realApplication": "NOT_RUN", "actualSdk": "NOT_RUN", "Windows": "NOT_RUN", "scope": "fixture protocol smoke only"}
    (root / "fixture-smoke-execution.json").write_text(json.dumps(record, indent=2), encoding="utf-8")
    print(json.dumps(record, indent=2))
    if error or process.returncode:
        raise SystemExit(1)


asyncio.run(main())
