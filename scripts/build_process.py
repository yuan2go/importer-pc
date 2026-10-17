"""Own every compiler/checker process tree until completion, failure or cancellation."""
import ctypes
import os
import signal
import subprocess
import time


class WindowsJob:
    """Kill-on-close job; no breakaway flag. Failure to acquire ownership fails the build."""
    def __init__(self):
        from ctypes import wintypes
        class IO_COUNTERS(ctypes.Structure):
            _fields_ = [(name, ctypes.c_ulonglong) for name in ('ReadOperationCount', 'WriteOperationCount', 'OtherOperationCount', 'ReadTransferCount', 'WriteTransferCount', 'OtherTransferCount')]
        class BASIC_LIMIT(ctypes.Structure):
            _fields_ = [('PerProcessUserTimeLimit', ctypes.c_longlong), ('PerJobUserTimeLimit', ctypes.c_longlong), ('LimitFlags', wintypes.DWORD), ('MinimumWorkingSetSize', ctypes.c_size_t), ('MaximumWorkingSetSize', ctypes.c_size_t), ('ActiveProcessLimit', wintypes.DWORD), ('Affinity', ctypes.c_size_t), ('PriorityClass', wintypes.DWORD), ('SchedulingClass', wintypes.DWORD)]
        class EXTENDED_LIMIT(ctypes.Structure):
            _fields_ = [('BasicLimitInformation', BASIC_LIMIT), ('IoInfo', IO_COUNTERS), ('ProcessMemoryLimit', ctypes.c_size_t), ('JobMemoryLimit', ctypes.c_size_t), ('PeakProcessMemoryUsed', ctypes.c_size_t), ('PeakJobMemoryUsed', ctypes.c_size_t)]
        self.api = ctypes.WinDLL('kernel32', use_last_error=True)
        self.api.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
        self.api.CreateJobObjectW.restype = wintypes.HANDLE
        self.api.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        self.api.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        self.api.CloseHandle.argtypes = [wintypes.HANDLE]
        self.handle = self.api.CreateJobObjectW(None, None)
        if not self.handle: raise ctypes.WinError(ctypes.get_last_error())
        limits = EXTENDED_LIMIT()
        limits.BasicLimitInformation.LimitFlags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not self.api.SetInformationJobObject(self.handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
            error = ctypes.WinError(ctypes.get_last_error()); self.close(); raise error

    def assign(self, process):
        if not self.api.AssignProcessToJobObject(self.handle, int(process._handle)):
            raise ctypes.WinError(ctypes.get_last_error())

    def resume(self, process):
        # Popen closes its primary thread handle. Enumerate the suspended process
        # using documented Toolhelp APIs, then resume only after job ownership.
        from ctypes import wintypes
        class THREADENTRY32(ctypes.Structure):
            _fields_ = [('dwSize', wintypes.DWORD), ('cntUsage', wintypes.DWORD), ('th32ThreadID', wintypes.DWORD), ('th32OwnerProcessID', wintypes.DWORD), ('tpBasePri', wintypes.LONG), ('tpDeltaPri', wintypes.LONG), ('dwFlags', wintypes.DWORD)]
        self.api.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
        self.api.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
        self.api.Thread32First.argtypes = [wintypes.HANDLE, ctypes.POINTER(THREADENTRY32)]
        self.api.Thread32Next.argtypes = [wintypes.HANDLE, ctypes.POINTER(THREADENTRY32)]
        self.api.OpenThread.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        self.api.OpenThread.restype = wintypes.HANDLE
        self.api.ResumeThread.argtypes = [wintypes.HANDLE]
        self.api.ResumeThread.restype = wintypes.DWORD
        snapshot = self.api.CreateToolhelp32Snapshot(4, 0)  # TH32CS_SNAPTHREAD
        if snapshot == ctypes.c_void_p(-1).value: raise ctypes.WinError(ctypes.get_last_error())
        try:
            entry = THREADENTRY32(); entry.dwSize = ctypes.sizeof(entry)
            present = self.api.Thread32First(snapshot, ctypes.byref(entry))
            while present:
                if entry.th32OwnerProcessID == process.pid:
                    thread = self.api.OpenThread(2, False, entry.th32ThreadID)  # THREAD_SUSPEND_RESUME
                    if not thread: raise ctypes.WinError(ctypes.get_last_error())
                    try:
                        if self.api.ResumeThread(thread) == 0xffffffff: raise ctypes.WinError(ctypes.get_last_error())
                    finally: self.api.CloseHandle(thread)
                    return
                present = self.api.Thread32Next(snapshot, ctypes.byref(entry))
            raise OSError('Suspended compiler primary thread not found')
        finally: self.api.CloseHandle(snapshot)

    def close(self):
        if self.handle:
            self.api.CloseHandle(self.handle)
            self.handle = None


def run(command, cwd=None):
    job = WindowsJob() if os.name == 'nt' else None
    process = None
    try:
        process = subprocess.Popen(command, cwd=cwd, start_new_session=os.name != 'nt',
                                   creationflags=(subprocess.CREATE_NEW_PROCESS_GROUP | 4) if os.name == 'nt' else 0)
        if job:
            try:
                job.assign(process)
                job.resume(process)
            except BaseException:
                # Assignment failure must not continue with unowned work.
                subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], check=False, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                raise
        code = process.wait()
        if code:
            raise subprocess.CalledProcessError(code, command)
    finally:
        # A process may spawn grandchildren. Terminate its entire owned group/job
        # even when the immediate process has already exited unsuccessfully.
        if job:
            job.close()
        elif process:
            try: os.killpg(process.pid, signal.SIGTERM)
            except ProcessLookupError: pass
            deadline = time.monotonic() + 2
            while process.poll() is None and time.monotonic() < deadline:
                time.sleep(.02)
            try: os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError: pass
        if process:
            process.wait()
