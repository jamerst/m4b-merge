using M4BMerge;
using Spectre.Console.Cli;

var app = new CommandApp<MergeCommand>();
return await app.RunAsync(args);