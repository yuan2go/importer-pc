# Windows构建进程所有权设计核验

核验日期：2026-10-18。实现源：089fd98的`scripts/build_process.py`。此记录仅为官方API语义及声明布局的静态复核，**Windows实际执行NOT_RUN**；不把文档查证写成取消/终止/句柄释放实机PASS。macOS真实子孙进程失败/取消验证见tooling-tests.log。

- Python的Popen在Windows调用CreateProcess，creationflags传递进程创建标志；POSIX的start_new_session在执行前建立会话。单进程terminate/kill不表达Windows完整子孙所有权，所以实现使用Job对象。[Python subprocess](https://docs.python.org/3/library/subprocess.html)
- CREATE_SUSPENDED=0x00000004使新进程主线程在ResumeThread前不运行。实现先挂起创建、再赋予Job所有权、最后恢复，避免先执行后赋予Job时子孙先行派生。[Microsoft Process Creation Flags](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags)
- AssignProcessToJobObject要求进程设置配额/终止权限；Windows8起支持嵌套Job，默认子进程加入Job链，未开启breakaway的Job保留其约束。赋予失败时构建明确失败，不能继续发布。[Microsoft AssignProcessToJobObject](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject)
- JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE在最后Job句柄关闭时终止所含进程；实现不设breakaway/silent-breakaway。该机制用于构建工具，不改变客户端业务停止/T3/资源合同。[Microsoft Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
- JOBOBJECT_BASIC_LIMIT_INFORMATION和EXTENDED_LIMIT_INFORMATION的字段顺序、DWORD/SIZE_T/ULONG_PTR与IO_COUNTERS映射按官方声明核对。此核对不等于在Windows x64验证ctypes实际布局/失败码。[Basic limit structure](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information) · [Extended limit structure](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_extended_limit_information)
- Toolhelp线程快照以TH32CS_SNAPTHREAD获取线程，Thread32First要求初始化dwSize；按th32OwnerProcessID选择尚挂起的目标主线程，用OpenThread的THREAD_SUSPEND_RESUME权限取得句柄，再ResumeThread。实际枚举、权限、Job嵌套、异常清理和返回码须纳入Windows后验。[CreateToolhelp32Snapshot](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/nf-tlhelp32-createtoolhelp32snapshot) · [Thread32First](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/nf-tlhelp32-thread32first) · [OpenThread](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-openthread) · [ResumeThread](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-resumethread)

现有Windows后验应额外执行：独立构建正常结束、编译失败、SIGINT/进程终止、在途子孙、Job赋予或线程恢复失败，核无成功包/无继续写入/无遗留子孙和句柄。未开通Windows环境、VM、runner或OAuth。
