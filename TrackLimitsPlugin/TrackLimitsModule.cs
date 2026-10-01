using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.Hosting;

namespace TrackLimitsPlugin;

public class TrackLimitsModule : AssettoServerModule<TrackLimitsConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<TrackLimitsService>().AsSelf().As<IHostedService>().SingleInstance();
    }
}
