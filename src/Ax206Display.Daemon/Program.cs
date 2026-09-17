using Ax206Display.Daemon.Composition;

var host = HostFactory.Create(args);
await host.RunAsync();
