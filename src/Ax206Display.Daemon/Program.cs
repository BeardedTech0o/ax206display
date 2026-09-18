using Ax206Display.Daemon.Composition;

var app = HostFactory.Create(args);
await app.RunAsync();
