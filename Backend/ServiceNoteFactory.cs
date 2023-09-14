using System.Collections.Generic;
using System.Linq;
using Mission.TimeTable.Domain.Interfaces.Services;
using Mission.TimeTable.Domain.Model.TimeTable;
using Mission.TimeTable.Domain.Services.ViewModels;
using Mission.TimeTable.Domain.Utility;

namespace Mission.TimeTable.Domain.Factories
{
    public class ServiceNoteFactory : IServiceNoteFactory
    {
        private const string ServicePrefix = "Service";

        public List<ServiceLevelNotes> GetOperatingPeriodNotes(IEnumerable<BusService> services, bool hasMultipleServiceInRouteGroup)
        {
            List<ServiceLevelNotes> operatingPeriodNotes = new List<ServiceLevelNotes>();

            foreach (var service in services.Where(e => e.HasOperatingProfiles))
            {
                var serviceLineName = hasMultipleServiceInRouteGroup ? " " + service.LineName : string.Empty;
                var message = $"{ServicePrefix}{serviceLineName} operates from {service.StartDate.ToFormattedDate()} until ";

                message += service.HasCloseEndDate ? service.EndDate.ToFormattedDate() : "further notice";

                operatingPeriodNotes.Add(new ServiceLevelNotes(message));
            }

            return operatingPeriodNotes.Where(e => !e.IsEmpty()).ToList();
        }

        public List<ServiceLevelNotes> GetServiceLevelNotes(IEnumerable<BusService> services, bool hasMultipleServiceInRouteGroup)
        {
            List<ServiceLevelNotes> operatingPeriodNotes = new List<ServiceLevelNotes>();

            foreach (var serviceConfiguration in services.Where(e => e.HasServiceNote && e.HasOperatingProfiles))
            {
                var message = serviceConfiguration.ServiceNote;
                if (hasMultipleServiceInRouteGroup)
                {
                    message = message.Replace(ServicePrefix, $"{ServicePrefix} {serviceConfiguration.LineName}");
                }

                operatingPeriodNotes.Add(new ServiceLevelNotes(message));
            }

            return operatingPeriodNotes.Where(e => !e.IsEmpty()).ToList();
        }
    }
}