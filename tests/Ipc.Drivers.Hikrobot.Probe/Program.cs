if (args is ["--bundled-list"]) { await BundledNativeVerification.Run(); return; }
if (args is ["--self-test"]) { await Verification.Run(); return; }
if (args is ["--mvs-self-test"]) { await MvsVerification.Run(); return; }
if (args.Length > 0 && args[0] is "--mvs-list" or "--mvs-connect" or "--mvs-throughput") { Environment.ExitCode = await MvsDiagnostic.Run(args); return; }

Environment.ExitCode = await Diagnostic.Run(args);
