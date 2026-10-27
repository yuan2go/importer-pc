if (args is ["--decoder-worker", var mode]) { await DecodingVerification.Worker(mode); return; }
if (args is ["--decoding-test", var root]) { await DecodingVerification.Run(root); return; }
if (args is ["--bundled-list"]) { await BundledNativeVerification.Run(); return; }
if (args is ["--self-test"]) { await Verification.Run(); return; }
if (args.Length > 0 && args[0] is "--mvs-list" or "--mvs-connect") { Environment.ExitCode = await MvsDiagnostic.Run(args); return; }
Environment.ExitCode = await Diagnostic.Run(args);
