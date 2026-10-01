using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.Hosting;

namespace ResultsPlugin;

public class ResultsModule : AssettoServerModule<ResultsConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ResultsWriter>().AsSelf().As<IHostedService>().SingleInstance();
    }
}
