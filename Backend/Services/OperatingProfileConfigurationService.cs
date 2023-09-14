using System;
using System.Collections.Generic;
using System.Linq;
using Mission.Crosstown.Model;
using Mission.TimeTable.Domain.Interfaces;
using Mission.TimeTable.Domain.Interfaces.Services;
using Mission.TimeTable.Domain.Model;
using Mission.TimeTable.Domain.Model.TimeTable;
using Mission.TimeTable.Domain.Model.TimeTable.Notes;

namespace Mission.TimeTable.Domain.Services
{
    public class OperatingProfileConfigurationService : IOperatingProfileConfigurationService
    {
        private readonly IOperatingProfileConfigurationRepository operatingProfileConfigurationRepository;
        private readonly IServiceRepository serviceRepository;
        private readonly IDayGroupService dayGroupService;
        private readonly IOperatorService operatorService;
        private readonly IOperatorColourService operatorColourService;

        public OperatingProfileConfigurationService(IOperatingProfileConfigurationRepository operatingProfileRepository, IServiceRepository serviceRepository, IDayGroupService dayGroupService, IOperatorService operatorService, IOperatorColourService operatorColourService)
        {
            this.operatingProfileConfigurationRepository = operatingProfileRepository;
            this.serviceRepository = serviceRepository;
            this.dayGroupService = dayGroupService;
            this.operatorService = operatorService;
            this.operatorColourService = operatorColourService;
        }

        public void UpdateOperatingProfileConfiguration(OperatingProfileConfiguration operatingProfileConfiguration)
        {
            this.operatingProfileConfigurationRepository.UpsertOperatingProfileConfiguration(operatingProfileConfiguration);
        }

        public List<OperatingProfileConfiguration> GetOperatingProfileConfigurationsByService(string operatorCode, string serviceCode)
        {
            return this.operatingProfileConfigurationRepository.GetOperatingProfileConfigurationsByOperatorCode(operatorCode, serviceCode).ToList();
        }

        public List<OperatingProfile> GetOperatingProfilesByService(string operatorCode, string serviceCode)
        {
            if (string.IsNullOrEmpty(serviceCode))
            {
                throw new NullReferenceException(nameof(serviceCode));
            }

            return this.serviceRepository.GetServices(new[] { new ServiceAndOperatorCode(serviceCode, operatorCode) }).First().OperatingProfiles.ToList();
        }

        public IList<BusService> GetServicesWithDayGroupApplied(IEnumerable<ServiceAndOperatorCode> serviceWithOperatorCode, IEnumerable<ServiceConfiguration> serviceConfigurations)
        {
            List<BusService> servicesResult = new List<BusService>();
            var dayGroups = this.dayGroupService.GetDayGroups();

            var operatingProfileConfigurations = this.operatingProfileConfigurationRepository.GetOperatingProfileConfigurations(serviceWithOperatorCode)
                .Where(x => x.HasDayGroupAssigned)
                .OrderBy(x => dayGroups.DayGroupIndex(x.DayGroupId))
                .ToArray();

            var services = this.serviceRepository.GetServices(serviceWithOperatorCode).ToList();

            var operatorColours = this.operatorColourService.GetAllColours();

            foreach (var service in services)
            {
                var amendment = serviceConfigurations.Single(e => e.ServiceCode == service.ServiceCode && e.OperatorCode == service.OperatorCode);
                service.ApplyChanges(amendment);

                BusService busService = new BusService(
                    service.ServiceCode,
                    service.OperatorCode,
                    service.LineName,
                    service.Destination,
                    service.StartDate,
                    service.EndDate,
                    service.Description,
                    service.ModificationDateTime,
                    service.ServiceNote);

                busService.AddJourneyPatternSections(service.JourneyPatternSections);

                var vehicleJourneyIDs = service.OperatingProfiles.Select(x => x.VehicleJourneyID);
                var serviceHasOperatingProfileConfigured = operatingProfileConfigurations.Any(e => e.ServiceCode == service.ServiceCode && vehicleJourneyIDs.Contains(e.VehicleJourneyId));

                if (serviceHasOperatingProfileConfigured)
                {
                    foreach (var configuration in operatingProfileConfigurations.Where(x =>
                        dayGroups.DayGroupInfo.Exists(c => c.DayGroupId == x.DayGroupId) && x.ServiceCode == service.ServiceCode))
                    {
                        var operatingProfile = service.OperatingProfiles.SingleOrDefault(e => e.VehicleJourneyID == configuration.VehicleJourneyId);

                        var vehicleJourneyIdIsStale = operatingProfile == null;

                        if (!vehicleJourneyIdIsStale)
                        {
                            var singleDayGroupNote = configuration.SingleDayGroupNote;
                            var excludedDayGroupNote = configuration.ExcludedDayGroupNote;

                            if (singleDayGroupNote.BackgroundColor.Length > 7)
                            {
                                var operatorColour = operatorColours.SingleOrDefault(x => x.Id == singleDayGroupNote.BackgroundColor);
                                singleDayGroupNote.BackgroundColor = operatorColour.Colour;
                            }

                            if (excludedDayGroupNote.BackgroundColor.Length > 7)
                            {
                                var operatorColour = operatorColours.SingleOrDefault(x => x.Id == excludedDayGroupNote.BackgroundColor);
                                excludedDayGroupNote.BackgroundColor = operatorColour.Colour;
                            }

                            List<Note> daygroupNotes = new List<Note>
                            {
                                new Model.TimeTable.Notes.SingleDayGroupNote(singleDayGroupNote.NoteText, singleDayGroupNote.Lookup, singleDayGroupNote.ShowLookup, singleDayGroupNote.UseColor, singleDayGroupNote.BackgroundColor),
                                new Model.TimeTable.Notes.ExcludedDayGroupNote(excludedDayGroupNote.NoteText, excludedDayGroupNote.Lookup, excludedDayGroupNote.ShowLookup, excludedDayGroupNote.UseColor, excludedDayGroupNote.BackgroundColor)
                            }.FindAll(e => e.HasNote);

                            foreach (var journey in operatingProfile.Journeys)
                            {
                                journey.AddJourneyNotes(daygroupNotes);
                            }

                            operatingProfile.SetDayGroupName(dayGroups.WithId(configuration.DayGroupId).DayGroupName);
                            busService.AddOperatingProfiles(new[] { operatingProfile });
                        }
                    }
                }

                servicesResult.Add(busService);
            }

            return servicesResult;
        }

        public List<string> GetServiceLineNamesUsedByDayGroup(string dayGroupId)
        {
            var operatingProfileConfiguration = this.operatingProfileConfigurationRepository.GetOperatingProfileConfigurationsByDayGroupId(dayGroupId);

            List<ServiceAndOperatorCode> serviceWithOperatorCode = new List<ServiceAndOperatorCode>();
            operatingProfileConfiguration.ForEach(x => serviceWithOperatorCode.Add(new ServiceAndOperatorCode(x.ServiceCode, x.OperatorCode)));

            return this.serviceRepository.GetServices(serviceWithOperatorCode).Select(x => $"{this.operatorService.GetOperator(x.OperatorCode).Name} - {x.LineName}").ToList();
        }
    }
}