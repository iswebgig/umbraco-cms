using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Umbraco.Core;
using Umbraco.Core.Events;
using Umbraco.Core.Logging;

namespace Umbraco.Web.Cache
{
    /// <summary>
    /// Default <see cref="IDistributedCacheBinder"/> implementation.
    /// </summary>
    public partial class DistributedCacheBinder : IDistributedCacheBinder
    {
        private static readonly ConcurrentDictionary<string, MethodInfo> FoundHandlers = new ConcurrentDictionary<string, MethodInfo>();
        private readonly DistributedCache _distributedCache;
        private readonly IUmbracoContextFactory _umbracoContextFactory;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="DistributedCacheBinder"/> class.
        /// </summary>
        public DistributedCacheBinder(DistributedCache distributedCache, IUmbracoContextFactory umbracoContextFactory, ILogger logger)
        {
            _distributedCache = distributedCache;
            _logger = logger;
            _umbracoContextFactory = umbracoContextFactory;
        }

        // internal for tests
        internal static MethodInfo FindHandler(IEventDefinition eventDefinition)
        {
            var name = eventDefinition.Sender.GetType().Name + "_" + eventDefinition.EventName;

            return FoundHandlers.GetOrAdd(name, n => CandidateHandlers.Value.FirstOrDefault(x => x.Name == n));
        }

        private static readonly Lazy<MethodInfo[]> CandidateHandlers = new Lazy<MethodInfo[]>(() =>
        {

            return typeof(DistributedCacheBinder)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(x =>
                {
                    if (x.Name.Contains("_") == false) return null;

                    var parts = x.Name.Split(Constants.CharArrays.Underscore, StringSplitOptions.RemoveEmptyEntries).Length;
                    if (parts != 2) return null;

                    var parameters = x.GetParameters();
                    if (parameters.Length != 2) return null;
                    if (typeof(EventArgs).IsAssignableFrom(parameters[1].ParameterType) == false) return null;
                    return x;
                })
                .WhereNotNull()
                .ToArray();
        });

        /// <inheritdoc />
        public void HandleEvents(IEnumerable<IEventDefinition> events)
        {
            // Ensure we run with an UmbracoContext, because this may run in a background task,
            // yet developers may be using the 'current' UmbracoContext in the event handlers.
            using (_umbracoContextFactory.EnsureUmbracoContext())
            {
                // When it comes to content types types, a change to any single one will trigger a reload of the content and media caches.
                // As far as I (AB) can tell, there's no type specific logic here, they all clear caches for all content types, and trigger a reload of all content and media.
                // We also have events registered for Changed and Saved, which do the same thing, so really only need one of these.
                // Hence if we have more than one document or media types, we can and should only handle one of the events for one, to avoid repeated cache reloads.
                foreach (var e in GetReducedEventList(events))
                {
                    var handler = FindHandler(e);
                    if (handler == null)
                    {
                        // TODO: should this be fatal (ie, an exception)?
                        var name = e.Sender.GetType().Name + "_" + e.EventName;
                        _logger.Warn<DistributedCacheBinder, string>("Dropping event {EventName} because no corresponding handler was found.", name);
                        continue;
                    }

                    handler.Invoke(this, new[] { e.Sender, e.Args });
                }
            }
        }

        // Internal for tests
        internal static IEnumerable<IEventDefinition> GetReducedEventList(IEnumerable<IEventDefinition> events)
        {
            var reducedEvents = new List<IEventDefinition>();

            var gotDoumentType = false;
            var gotMediaType = false;
            var gotMemberType = false;

            foreach (var evt in events)
            {
                if (evt.Sender.ToString().Contains(nameof(Core.Services.Implement.ContentTypeService)))
                {
                    if (gotDoumentType == false)
                    {
                        reducedEvents.Add(evt);
                        gotDoumentType = true;
                    }
                }
                else if (evt.Sender.ToString().Contains(nameof(Core.Services.Implement.MediaTypeService)))
                {
                    if (gotMediaType == false)
                    {
                        reducedEvents.Add(evt);
                        gotMediaType = true;
                    }
                }
                else if (evt.Sender.ToString().Contains(nameof(Core.Services.Implement.MemberTypeService)))
                {
                    if (gotMemberType == false)
                    {
                        reducedEvents.Add(evt);
                        gotMemberType = true;
                    }
                }
                else
                {
                    reducedEvents.Add(evt);
                }
            }

            return reducedEvents;
        }
    }
}
