using LabelBridge;

if (args.Length > 0 && args[0] == "test-print")
    return await TestPrint.RunAsync(args[1..]);

var app = AppBuilder.Build(args);
await app.RunAsync();
return 0;
