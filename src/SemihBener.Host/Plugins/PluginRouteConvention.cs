using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.RateLimiting;
using SemihBener.Abstractions;
using SemihBener.Host.Core;

namespace SemihBener.Host.Plugins;

/// <summary>
/// Plugin controller'larına host tarafından zorla uygulanan kurallar:
///  - Route öneki /api/{key} (plugin başka bir uygulamanın route'unu ezemez)
///  - Sadece kendi plugin'inin auth şeması (token'lar plugin'ler arasında geçmez)
///  - Plugin'e özel rate limit politikası ([StrictRateLimit] olanlarda sıkı politika)
///  - Swagger'da plugin'in kendi dokümanı
/// </summary>
public sealed class PluginRouteConvention(PluginCatalog catalog) : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            var plugin = catalog.FindByAssembly(controller.ControllerType.Assembly);
            if (plugin is null)
            {
                continue;
            }

            var prefix = new AttributeRouteModel(new RouteAttribute($"api/{plugin.Key}"));
            foreach (var selector in controller.Selectors)
            {
                selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(
                    prefix,
                    selector.AttributeRouteModel ?? new AttributeRouteModel(new RouteAttribute("[controller]")));
            }

            controller.ApiExplorer.GroupName = plugin.Key;
            controller.ApiExplorer.IsVisible = true;

            var controllerIsStrict = controller.Attributes.OfType<StrictRateLimitAttribute>().Any();

            foreach (var action in controller.Actions)
            {
                var strict = controllerIsStrict || action.Attributes.OfType<StrictRateLimitAttribute>().Any();
                var policy = strict ? RateLimitPolicies.Strict(plugin.Key) : RateLimitPolicies.Standard(plugin.Key);

                foreach (var selector in action.Selectors)
                {
                    selector.EndpointMetadata.Add(new EnableRateLimitingAttribute(policy));

                    if (plugin.Record.AuthKind != PluginAuthKind.None)
                    {
                        selector.EndpointMetadata.Add(new AuthorizeAttribute
                        {
                            AuthenticationSchemes = AuthSchemes.ForPlugin(plugin.Key)
                        });
                    }
                }
            }
        }
    }
}
