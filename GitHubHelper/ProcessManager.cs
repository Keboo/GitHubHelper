using System;
using System.CommandLine;
using System.CommandLine.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GitHubHelper
{
    public static class ProcessManagerMixins
    {
        public static async Task<bool> RunNugetCommand(this IProcessManager processManager, FileInfo? nuget, string command, DirectoryInfo nugetDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = nuget?.FullName ?? "nuget",
                Arguments = command,
                WorkingDirectory = nugetDirectory.FullName
            };
            return await processManager.RunProcessAsync(startInfo, x => Console.WriteLine(x), null, CancellationToken.None) == 0;
        }
    }

    public interface IProcessManager
    {
        Task<int> RunProcessAsync(
            ProcessStartInfo startInfo,
            Action<string>? progressOutput,
            Action<string>? progressError,
            CancellationToken token);
    }

    public class ProcessManager : IProcessManager
    {
        private readonly IConsole _Logger;
        public ProcessManager(IConsole logger)
        {
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<int> RunProcessAsync(
            ProcessStartInfo startInfo,
            Action<string>? progressOutput,
            Action<string>? progressError,
            CancellationToken token)
        {
            if (startInfo is null)
            {
                throw new ArgumentNullException(nameof(startInfo));
            }

            return Task.Factory.StartNew(() =>
            {
                Process process = RunProcessInternal(startInfo, progressOutput, progressError, token);
                return process.ExitCode;
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Current);
        }

        private Process RunProcessInternal(
            ProcessStartInfo startInfo,
            Action<string>? progressOutput,
            Action<string>? progressError,
            CancellationToken token)
        {
            var process = new Process
            {
                StartInfo = UpdateProcessStartInfo(startInfo)
            };
            return RunProcessInternal(process, progressOutput, progressError, token);
        }

        private Process RunProcessInternal(
            Process process,
            Action<string>? progressOutput,
            Action<string>? progressError,
            CancellationToken token)
        {
            _Logger.Out.WriteLine($"Running process: '{process.StartInfo.FileName} {process.StartInfo.Arguments}'");
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += OutputHandler;
            process.ErrorDataReceived += ErrorHandler;

            try
            {
                if (!process.Start())
                {
                    return process;
                }

                token.Register(obj =>
                {
                    if (obj is Process p && !p.HasExited)
                    {
                        try
                        {
                            p.Kill();
                        }
                        catch (Win32Exception ex)
                        {
                            _Logger.Error.WriteLine($"Error cancelling process{Environment.NewLine}{ex}");
                        }
                    }
                }, process);


                if (process.StartInfo.RedirectStandardOutput)
                {
                    process.BeginOutputReadLine();
                }
                if (process.StartInfo.RedirectStandardError)
                {
                    process.BeginErrorReadLine();
                }

                // Create OS job object to be killed when we exit or are killed
                try
                {
                    if (!JobManager.AttachProcessToJob(process))
                    {
                        _Logger.Out.WriteLine("Failed to attach process to job");
                    }
                }
                catch (ProcessJobException e)
                {
                    _Logger.Error.WriteLine($"Error attaching process to job{Environment.NewLine}{e}");
                }

                if (process.HasExited)
                {
                    return process;
                }
                process.WaitForExit();
            }
            catch (Exception e)
            {
                _Logger.Error.WriteLine($"Error running '{process.StartInfo.FileName} {process.StartInfo.Arguments}'{Environment.NewLine}{e}");
            }
            finally
            {
                if (process.StartInfo.RedirectStandardError)
                {
                    process.CancelErrorRead();
                }
                if (process.StartInfo.RedirectStandardOutput)
                {
                    process.CancelOutputRead();
                }
                process.OutputDataReceived -= OutputHandler;
                process.ErrorDataReceived -= ErrorHandler;

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch (Exception ex)
                {
                    _Logger.Error.WriteLine($"Unable to kill process '{process.StartInfo.FileName} {process.StartInfo.Arguments}'{Environment.NewLine}{ex}");
                }
            }
            return process;

            void OutputHandler(object s, DataReceivedEventArgs e)
            {
                progressOutput?.Invoke(e.Data);
            }

            void ErrorHandler(object s, DataReceivedEventArgs e)
            {
                progressError?.Invoke(e.Data);
            }
        }

        private static ProcessStartInfo UpdateProcessStartInfo(ProcessStartInfo startInfo)
        {
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.UseShellExecute = false;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;

            return startInfo;
        }

        // ReSharper disable InconsistentNaming
        // ReSharper disable IdentifierTypo
        // ReSharper disable StringLiteralTypo
        private static class JobManager
        {
            private static readonly Version Windows8Version = new Version(6, 2, 0, 0);

            public static bool AttachProcessToJob(Process process)
            {
                if (process == null) throw new ArgumentNullException(nameof(process));

                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return false;
                }

                if (!CanAssignProcessToJob(process))
                {
                    return false;
                }

                IntPtr jobPtr = CreateJobObject(IntPtr.Zero, null);
                if (jobPtr == IntPtr.Zero)
                {
                    throw new ProcessJobException(nameof(CreateJobObject));
                }

                var jobObjectInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = { LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE }
                };

                if (!SetInformationJobObject(jobPtr,
                    JobObject.JobObjectExtendedLimitInformation,
                    ref jobObjectInfo,
                    (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
                {
                    throw new ProcessJobException(nameof(SetInformationJobObject));
                }

                if (!AssignProcessToJobObject(jobPtr, process.Handle))
                {
                    throw new ProcessJobException(nameof(AssignProcessToJobObject));
                }

                return true;
            }

            private static bool CanAssignProcessToJob(Process process)
            {
                if (Environment.OSVersion.Version >= Windows8Version)
                {
                    //Window 8 and newer support desting of jobs. So we can always attach.
                    return true;
                }

                if (!IsProcessInJob(process.Handle, IntPtr.Zero, out bool isInJob))
                {
                    throw new ProcessJobException(nameof(IsProcessInJob));
                }
                return !isInJob;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool IsProcessInJob([In] IntPtr ProcessHandle, [In] IntPtr JobHandle, [Out] out bool Result);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern IntPtr CreateJobObject([In] IntPtr lpJobAttributes, [In] string? lpName);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool SetInformationJobObject([In] IntPtr hJob,
                [In] JobObject JobObjectInfoClass,
                [In] ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo,
                [In] uint cbJobObjectInfoLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool AssignProcessToJobObject([In] IntPtr hJob, [In] IntPtr hProcess);

            private enum JobObject
            {
                //JobObjectBasicLimitInformation = 2,
                //JobObjectBasicUIRestrictions = 4,
                //JobObjectSecurityLimitInformation,
                //JobObjectEndOfJobTimeInformation,
                //JobObjectAssociateCompletionPortInformation,
                JobObjectExtendedLimitInformation = 9,
                //JobObjectGroupInformation = 11,
                //JobObjectNotificationLimitInformation,
                //JobObjectGroupInformationEx = 14,
                //JobObjectCpuRateControlInformation
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                // ReSharper disable MemberCanBePrivate.Local
                // ReSharper disable FieldCanBeMadeReadOnly.Local
                public IO_COUNTERS IoInfo;
                public IntPtr ProcessMemoryLimit;
                public IntPtr JobMemoryLimit;
                public IntPtr PeakProcessMemoryUsed;
                public IntPtr PeakJobMemoryUsed;
                // ReSharper restore FieldCanBeMadeReadOnly.Local
                // ReSharper restore MemberCanBePrivate.Local
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct IO_COUNTERS
            {
                // ReSharper disable MemberCanBePrivate.Local
                // ReSharper disable FieldCanBeMadeReadOnly.Local
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
                // ReSharper restore FieldCanBeMadeReadOnly.Local
                // ReSharper restore MemberCanBePrivate.Local
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                // ReSharper disable FieldCanBeMadeReadOnly.Local
                // ReSharper disable MemberCanBePrivate.Local
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public JOB_OBJECT_LIMIT LimitFlags;
                public IntPtr MinimumWorkingSetSize;
                public IntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public IntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
                // ReSharper restore MemberCanBePrivate.Local
                // ReSharper restore FieldCanBeMadeReadOnly.Local
            }

            [Flags]
            private enum JOB_OBJECT_LIMIT : uint
            {

                //JOB_OBJECT_LIMIT_WORKINGSET = 0x00000001,
                //JOB_OBJECT_LIMIT_PROCESS_TIME = 0x00000002,
                //JOB_OBJECT_LIMIT_JOB_TIME = 0x00000004,
                //JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008,

                //JOB_OBJECT_LIMIT_AFFINITY = 0x00000010,
                //JOB_OBJECT_LIMIT_PRIORITY_CLASS = 0x00000020,
                //JOB_OBJECT_LIMIT_PRESERVE_JOB_TIME = 0x00000040,
                //JOB_OBJECT_LIMIT_SCHEDULING_CLASS = 0x00000080,
                //JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100,
                //JOB_OBJECT_LIMIT_JOB_MEMORY = 0x00000200,
                //JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x00000400,
                //JOB_OBJECT_LIMIT_BREAKAWAY_OK = 0x00000800,
                //JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK = 0x00001000,
                JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000,
                //JOB_OBJECT_LIMIT_SUBSET_AFFINITY = 0x00004000
            }
        }
        // ReSharper restore InconsistentNaming
        // ReSharper restore IdentifierTypo
        // ReSharper restore StringLiteralTypo
    }

    public class ProcessJobException : Exception
    {
        public ProcessJobException(string methodName)
            : base($"{methodName} failed with code {Marshal.GetLastWin32Error()}")
        { }
    }
}