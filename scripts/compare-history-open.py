#!/usr/bin/env python3
"""Compare exact parent/candidate production closures with one public-API harness.

Build and freeze each clean commit first; overlay the SAME verification DLL,
deps.json and runtimeconfig.json on separate copies of both build outputs.
Create one synthetic corpus with --history-open-fixture before invoking this.
Only the C# public constructor is timed; no arbitrary performance PASS threshold.
"""
import argparse
import hashlib
import json
import os
import queue
import statistics
import subprocess
import threading
from pathlib import Path


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


class Worker:
    def __init__(self, name, runtime, corpus, output):
        self.name = name
        self.errors = Path(str(output) + "." + name + ".stderr.txt").open("x", encoding="utf-8")
        self.process = subprocess.Popen(
            ["dotnet", str(runtime / "Ipc.Application.Verification.dll"), ".",
             "--history-open-worker", str(corpus)], stdin=subprocess.PIPE,
            stdout=subprocess.PIPE, stderr=self.errors, text=True, encoding="utf-8")
        self.lines = queue.Queue()
        def collect():
            for line in self.process.stdout:
                self.lines.put(line)
            self.lines.put(None)
        threading.Thread(target=collect, daemon=True).start()

    def read(self):
        line = self.lines.get(timeout=120)
        if line is None:
            raise RuntimeError(self.name + " worker ended; inspect its stderr evidence")
        return json.loads(line)

    def send(self, command):
        self.process.stdin.write(command + "\n")
        self.process.stdin.flush()
        result = self.read()
        if result.get("Kind") != command or result.get("OriginalsUnchanged") is not True:
            raise RuntimeError("Worker did not preserve original facts: " + repr(result))
        return result

    def close(self):
        try:
            if self.process.poll() is None:
                try:
                    self.process.stdin.write("quit\n")
                    self.process.stdin.flush()
                except BrokenPipeError:
                    pass  # A failed owned child still has its stderr and exit code.
                try:
                    self.process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    # Only this controller's dedicated test child, never an app/helper.
                    self.process.kill()
                    self.process.wait(timeout=15)
            return self.process.returncode
        finally:
            self.errors.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("baseline", "candidate", "corpus", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("baseline-commit", "candidate-commit"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    paths = {name: getattr(args, name).resolve() for name in ("baseline", "candidate")}
    corpus, output = args.corpus.resolve(), args.output.resolve()
    if output.exists() or paths["baseline"] == paths["candidate"]:
        raise ValueError("Separate frozen runtimes and a new evidence output are required")
    output.parent.mkdir(parents=True, exist_ok=True)
    for suffix in ("dll", "deps.json", "runtimeconfig.json"):
        filename = "Ipc.Application.Verification." + suffix
        if digest(paths["baseline"] / filename) != digest(paths["candidate"] / filename):
            raise ValueError("Harness/dependency/runtime identity differs: " + filename)
    report = {"Scope": "2000 synthetic originals; constructor only; empty restore set; no device/24h qualification",
              "Timing": "one warmup each, AB x5 then BA x5; budget/hash/disposal outside timer",
              "RuntimeEnvironment": {key: os.environ.get(key, "runtime default") for key in
                                     ("DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_ReadyToRun")},
              "Sources": {"baseline": args.baseline_commit, "candidate": args.candidate_commit},
              "Closures": {name: {str(p.relative_to(path)): digest(p) for p in sorted(path.rglob("*.dll"))}
                           for name, path in paths.items()}, "Ready": {}, "Warmups": {}, "Samples": []}
    workers = {}
    success = False
    try:
        for name, path in paths.items():
            worker = Worker(name, path, corpus, output)
            workers[name] = worker
            ready = worker.read()
            if ready.get("Kind") != "ready" or ready.get("RuntimeVersion") != "1.0.0+" + report["Sources"][name]:
                raise ValueError("Production source identity differs: " + repr(ready))
            if ready["RuntimeSha256"].lower() != digest(path / "Ipc.Application.ParcelFlow.dll"):
                raise ValueError("Loaded production DLL differs from frozen output")
            report["Ready"][name] = ready
        a, b = (report["Ready"][name] for name in paths)
        for key in ("Facts", "HarnessSha256", "Framework", "OS", "Architecture", "Processors"):
            if a[key] != b[key]:
                raise ValueError("Comparison context differs: " + key)
        for name, worker in workers.items():
            report["Warmups"][name] = worker.send("warmup")
        for pair in range(10):
            order = ("baseline", "candidate") if pair < 5 else ("candidate", "baseline")
            for name in order:
                report["Samples"].append({"Pair": pair + 1, "Order": "AB" if pair < 5 else "BA",
                                          "Runtime": name, **workers[name].send("sample")})
        report["Statistics"] = {}
        for name in paths:
            values = [x["Milliseconds"] for x in report["Samples"] if x["Runtime"] == name]
            report["Statistics"][name] = {"Count": len(values), "MedianMs": statistics.median(values),
                                          "MinimumMs": min(values), "MaximumMs": max(values)}
        old, new = (report["Statistics"][name]["MedianMs"] for name in paths)
        report["NetMedianReductionPercent"] = 100 * (old - new) / old
        report["Interpretation"] = "Observed net candidate difference, including added guard/observer cost; not a capacity or SLA claim"
        success = True
    except Exception as error:
        report["Failure"] = repr(error)
        raise
    finally:
        report["WorkerExitCodes"] = {name: worker.close() for name, worker in workers.items()}
        report["Complete"] = success and all(code == 0 for code in report["WorkerExitCodes"].values())
        output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    if not report["Complete"]:
        raise RuntimeError("Benchmark child failed; inspect preserved evidence")
    print(json.dumps({"Statistics": report["Statistics"], "NetMedianReductionPercent": report["NetMedianReductionPercent"]}))


if __name__ == "__main__":
    main()
