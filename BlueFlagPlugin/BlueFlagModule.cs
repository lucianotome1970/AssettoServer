using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.Hosting;

namespace BlueFlagPlugin;

public class BlueFlagModule : AssettoServerModule<BlueFlagConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<BlueFlagService>().AsSelf().As<IHostedService>().SingleInstance();
    }
}
