using Civ6Async.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("civ6-async");

    config.AddCommand<InstallCommand>("install")
        .WithDescription("Install or update the civ6-async mod into Civilization VI's Mods folder.");

    config.AddCommand<UninstallCommand>("uninstall")
        .WithDescription("Remove the civ6-async mod from Civilization VI's Mods folder.");

    config.AddCommand<StatusCommand>("status")
        .WithDescription("Show whether the mod is installed and where.");

    config.AddCommand<HealthCommand>("health")
        .WithDescription("Run a full set of checks against the install.");
});

return await app.RunAsync(args);
