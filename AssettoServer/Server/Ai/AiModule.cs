using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.OpenSlotFilters;
using Autofac;
using Microsoft.Extensions.Hosting;

namespace AssettoServer.Server.Ai;

public class AiModule : Module
{
    private readonly ACServerConfiguration _configuration;

    public AiModule(ACServerConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AiState>().AsSelf();

        if (_configuration.Extra.EnableAi)
        {
            builder.RegisterType<AiBehavior>().AsSelf().As<IHostedService>().SingleInstance();
            builder.RegisterType<AiUpdater>().AsSelf().SingleInstance().AutoActivate();
            builder.RegisterType<AiSlotFilter>().As<IOpenSlotFilter>();
            
            if (_configuration.Extra.AiParams.HourlyTrafficDensity != null)
            {
                builder.RegisterType<DynamicTrafficDensity>().As<IHostedService>().SingleInstance();
            }

        }

        // THE SPLINE IS NOT ONLY FOR TRAFFIC.
        //
        // It is the only description of track geometry a server has without
        // loading the track mesh, and plugins that judge where the circuit
        // ends need it on servers that run no AI at all.
        if (_configuration.Extra.EnableAi || _configuration.Extra.LoadAiSplineWithoutAi)
        {
            builder.RegisterType<AiSplineWriter>().AsSelf();
            builder.RegisterType<FastLaneParser>().AsSelf();
            builder.RegisterType<AiSplineLocator>().AsSelf();
            builder.Register((AiSplineLocator locator) => locator.Locate()).AsSelf().SingleInstance();
        }
    }
}
