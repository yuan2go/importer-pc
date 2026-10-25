using Ipc.Assembly;
using Ipc.Platform;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Readonly Windows deployment review");
if (args.Length != 2) throw new ArgumentException("<frozen-media> <new-private-evidence-directory>");
DeploymentBatchVerification.Run(DeploymentMedia.Inspect(Path.GetFullPath(args[0]), ParcelStartupServices.ApplicationId), Path.GetFullPath(args[1]));
