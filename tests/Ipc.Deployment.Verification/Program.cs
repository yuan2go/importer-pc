using Ipc.Assembly;
using Ipc.Platform;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Readonly Windows deployment review");
if (args.Length != 3) throw new ArgumentException("<frozen-media> <explicit-configuration-directory> <new-private-evidence-directory>");
DeploymentBatchVerification.Run(DeploymentMedia.Inspect(Path.GetFullPath(args[0]), ParcelStartupServices.ApplicationId), Path.GetFullPath(args[2]), Path.GetFullPath(args[1]));
