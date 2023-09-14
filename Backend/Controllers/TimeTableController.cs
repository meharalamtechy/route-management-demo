using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Mission.Crosstown.Model;
using Mission.TimeTable.DAL.Enum;
using Mission.TimeTable.DAL.Interfaces;
using Mission.TimeTable.DAL.Utility;
using Mission.TimeTable.Domain.Interfaces.Services;
using Mission.TimeTable.Domain.Model;
using Mission.TimeTable.Domain.Model.Comparer;
using Mission.TimeTable.Domain.Model.TimeTable;
using Mission.TimeTable.Domain.Model.TimeTable.Routes;
using Mission.TimeTable.Domain.Response;
using Mission.TimeTable.Domain.Utility;
using Mission.TimeTable.Domain.ViewModel;
using Mission.TimeTable.Model.Summary;
using Mission.TimeTable.Model.TimeTable;
using Mission.TimeTable.Web.Filters;
using Mission.TimeTable.Web.Model;
using Mission.TimeTable.Web.Model.Account;
using Mission.TimeTable.Web.Model.TimeTable;

namespace Mission.TimeTable.Controllers
{
    [Route("api/timetable")]
    [Authorize(Policy = "RoleVerification", Roles = UserRole.Admin + " , " + UserRole.Standard)]
    [ServiceFilter(typeof(CustomerDatabaseFilter))]
    public class TimeTableController : Controller
    {
        private readonly ITimoService timoService;
        private readonly IBusStopService busStopService;
        private readonly IBusServiceService busService;
        private readonly IExcelService excelService;
        private readonly ITimeTableHttpClient client;
        private readonly Func<IAuthenticationProvider> authenticationProvider;

        public TimeTableController(
            IBusStopService busStopService,
            IExcelService excelService,
            ITimoService timoService,
            IBusServiceService busService,
            ITimeTableHttpClient client,
            Func<IAuthenticationProvider> authenticationProvider)
        {
            this.busService = busService;
            this.busStopService = busStopService;
            this.excelService = excelService;
            this.timoService = timoService;
            this.client = client;
            this.authenticationProvider = authenticationProvider;
        }

        [HttpPost]
        [Route("Validate/Stops")]
        public IActionResult ValidateStops([FromBody] string[] stopCodes)
        {
            if (stopCodes == null)
            {
                throw new ArgumentNullException(nameof(stopCodes));
            }

            string[] stopPoints = stopCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(e => e.Trim()).ToArray();
            var stops = this.busStopService.ValidateStops(stopPoints);

            return this.Ok(stops);
        }

        [HttpPost]
        [Route("ValidateFile")]
        public IActionResult ValidateFile(IFormFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            var excelStops = this.excelService.LoadStops(file.OpenReadStream()).ToArray();
            excelStops = excelStops.Where(w => w.StopCode != string.Empty).ToArray();
            var stopPoints = excelStops.Select(e => e.StopCode.ToString().Trim());

            var stops = this.busStopService.ValidateStops(stopPoints).ToList();

            stops.ForEach(x =>
            {
                var excelStop = excelStops.First(e => e.StopCode == x.Data.StopCode || e.NaptanCode == x.Data.NaptanCode);
                x.Data.SetCaseInformation(excelStop.CaseQty, excelStop.Size);
            });

            return this.Ok(stops);
        }

        [Route("StopJourneys")]
        public IActionResult GetStopJourneys(string stopCode, string timoId)
        {
            if (string.IsNullOrEmpty(stopCode))
            {
                throw new ArgumentNullException(nameof(stopCode));
            }

            if (string.IsNullOrEmpty(timoId))
            {
                throw new ArgumentNullException(nameof(timoId));
            }

            var timo = this.timoService.GetTimo(timoId);
            var startDate = timo.ActivePeriod.StartDate;
            var endDate = timo.ActivePeriod.EndDate;
            var stop = this.timoService.GetTimoStops(timo).First(x => x.StopCode.Equals(stopCode));

            StopInformationViewModel stopInfo = new StopInformationViewModel(stop.StopCode.Value, stop.StopName, stop.NaptanCode);

            var stopCase = timo.BusStops.First(x => x.StopCode.Equals(stopCode)).Cases[0];
            var services = this.busService.GetServices(stopCase.Services.Select(z => new ServiceAndOperatorCode(z.ServiceCode, z.OperatorCode))).ToList();
            List<ServiceViewModel> servicesModel = new List<ServiceViewModel>();

            var routes = stopCase.RouteGroups.SelectMany(e => e.RoutesDetail);

            services.Where(x => !((x.StartDate < startDate && x.EndDate < startDate) || (x.StartDate > endDate && x.EndDate > endDate)))
                .ToList().ForEach(x =>
                   {
                       var operatingProfilesModel = x.OperatingProfiles.Where(e => e.GetActive(startDate, endDate)).Select(e =>
                                             new OperatingProfileViewModel(
                                             e.VehicleJourneyID,
                                             e.Name,
                                             new ServicedOrganizationDayTypeViewModel(
                                             e.ServicedOrganizationDayType.OrganizationName,
                                             new ServiceOrganizationDayOperationViewModel(
                                         e.ServicedOrganizationDayType.DaysOfOperation.Holidays,
                                         e.ServicedOrganizationDayType.DaysOfOperation.WorkingDays.Select(m => new DateRangesViewModel(m.StartTime, m.EndTime))),
                                         new ServiceOrganizationDayOperationViewModel(
                                         e.ServicedOrganizationDayType.DaysOfOperation.Holidays,
                                         e.ServicedOrganizationDayType.DaysOfOperation.WorkingDays.Select(m => new DateRangesViewModel(m.StartTime, m.EndTime)))),
                                         new SpecialDayOperationViewModel(
                                         e.SpecialDaysOperation.DaysOfNonOperation.Select(m => new DateRangesViewModel(m.StartTime, m.EndTime)),
                                         e.SpecialDaysOperation.DaysOfOperation.Select(m => new DateRangesViewModel(m.StartTime, m.EndTime))),
                                         new BankHolidaysOperationViewModel(e.BankHolidaysOperations.DaysOfOperations, e.BankHolidaysOperations.DaysOfNonOperations),
                                             e.Journeys.Where(j => routes.Any(r => r.RouteRef == j.RouteRef))
                                             .SelectMany(m =>
                                                 {
                                                     return m.GetDepartureTimes(stopCode).Select(d => new JourneyViewModel(
                                                         d.ToString(),
                                                         m.TxcNote.Code,
                                                         m.TxcNote.Text));
                                                 })
                                             .Where(m => m.HasDeparture).OrderBy(d => d.DepartureTime)))
                                      .Where(e => e.Journeys.Count > 0).ToList();

                       servicesModel.Add(new ServiceViewModel(x.ServiceCode, x.OperatorCode, x.LineName, operatingProfilesModel, false, x.Destination, string.Empty, string.Empty));
                   });

            return this.Ok(new StopAndServicesViewModel { Stop = stopInfo, Services = servicesModel.Where(e => e.HasOperatingProfiles).ToList() });
        }

        [Route("AdditionalInformation")]
        public IActionResult AdditionalInformation(string vehicleJourneyId, string serviceCode, string operatorCode)
        {
            if (string.IsNullOrEmpty(vehicleJourneyId))
            {
                throw new ArgumentNullException(nameof(vehicleJourneyId));
            }

            if (string.IsNullOrEmpty(serviceCode))
            {
                throw new ArgumentNullException(nameof(serviceCode));
            }

            if (string.IsNullOrEmpty(operatorCode))
            {
                throw new ArgumentNullException(nameof(operatorCode));
            }

            var operatingProfile = this.busService.GetAdditionalInformation(vehicleJourneyId, new ServiceAndOperatorCode(serviceCode, operatorCode));

            OperatingProfileViewModel operatingProfileModel = new OperatingProfileViewModel(
                operatingProfile.VehicleJourneyID,
                operatingProfile.RegularDayOperation.JoinWithComma(),
                new ServicedOrganizationDayTypeViewModel(
                    operatingProfile.ServicedOrganizationDayType.OrganizationName,
                    new ServiceOrganizationDayOperationViewModel(
                    operatingProfile.ServicedOrganizationDayType.DaysOfNonOperation.Holidays.OrderBy(e => e),
                    operatingProfile.ServicedOrganizationDayType.DaysOfNonOperation.WorkingDays.OrderBy(e => e.StartTime).Select(e => new DateRangesViewModel(e.StartTime, e.EndTime))),
                    new ServiceOrganizationDayOperationViewModel(
                    operatingProfile.ServicedOrganizationDayType.DaysOfOperation.Holidays.OrderBy(e => e),
                    operatingProfile.ServicedOrganizationDayType.DaysOfOperation.WorkingDays.OrderBy(e => e.StartTime).Select(e => new DateRangesViewModel(e.StartTime, e.EndTime)))),
                    new SpecialDayOperationViewModel(
                    operatingProfile.SpecialDaysOperation.DaysOfNonOperation.OrderBy(e => e.StartTime).Select(e => new DateRangesViewModel(e.StartTime, e.EndTime)),
                    operatingProfile.SpecialDaysOperation.DaysOfOperation.OrderBy(e => e.StartTime).Select(e => new DateRangesViewModel(e.StartTime, e.EndTime))),
                    new BankHolidaysOperationViewModel(operatingProfile.BankHolidaysOperations.DaysOfOperations.OrderBy(e => e), operatingProfile.BankHolidaysOperations.DaysOfNonOperations.OrderBy(e => e)),
                    new JourneyViewModel[0]);
            return this.Ok(operatingProfileModel);
        }

        [HttpPost]
        [Route("CheckAvailableServices")]
        public IActionResult CheckAvailableServices(string timoId, [FromBody] List<StopViewModel> stops)
        {
            string warningText = "<ul>";
            var timo = this.timoService.GetTimo(timoId);

            foreach (var stop in stops)
            {
                var stopInfo = this.busStopService.GetStops(new[] { new AtcoCode(stop.StopCode) }).First();
                var services = this.busService.GetServicesSummary(stopInfo.Services, timo.ActivePeriod.StartDate, timo.ActivePeriod.EndDate);
                if (services.Count == 0)
                {
                    warningText += "<li>" + stop.StopName + " has no available services.</li>";
                }
            }

            warningText += "</ul>";
            return this.Ok(warningText);
        }

        [Route("StopServices")]
        public IActionResult StopServices(string stopCode, string timoId, int stopCaseIndex)
        {
            var timo = this.timoService.GetTimo(timoId);
            var stopInfo = this.busStopService.GetStops(new[] { new AtcoCode(stopCode) }).First();
            var services = this.busService.GetServicesSummary(stopInfo.Services, timo.ActivePeriod.StartDate, timo.ActivePeriod.EndDate);
            var currentTimoStop = timo.BusStops.First(x => x.StopCode.Equals(stopCode));

            var timoServices = currentTimoStop.Cases[stopCaseIndex].Services;

            var serviceDetails = stopInfo.Services.Where(e => e.HasDeparture).SelectMany(x =>
            {
                var servicesByDirection = new List<ServiceByDirectionSummary>();
                var serviceInfo = services.FirstOrDefault(e => e.ServiceCode == x.ServiceCode && e.OperatorCode == x.OperatorCode);
                if (serviceInfo != null)
                {
                    var timoServiceInfo = timoServices.FirstOrDefault(k => k.OperatorCode == x.OperatorCode && k.ServiceCode == x.ServiceCode);
                    bool serviceIsSelected;

                    if (x.HasInboundDepartures)
                    {
                        serviceIsSelected = timoServiceInfo != null && timoServiceInfo.Inbound;
                        this.ConvertToServiceByDirectionSummary(x, servicesByDirection, serviceInfo, new Inbound(), serviceIsSelected);
                    }

                    if (x.HasOutboundDepartures)
                    {
                        serviceIsSelected = timoServiceInfo != null && timoServiceInfo.Outbound;
                        this.ConvertToServiceByDirectionSummary(x, servicesByDirection, serviceInfo, new Outbound(), serviceIsSelected);
                    }
                }

                return servicesByDirection;
            }).OrderBy(e => e.LineName.PadNumbers()).ToList();

            return this.Ok(serviceDetails);
        }

        [HttpPost]
        [Route("StopsServices")]
        public IActionResult StopsServices(string timoId, [FromBody] List<string> stopCodes)
        {
            var timo = this.timoService.GetTimo(timoId);
            var stopInfo = this.busStopService.GetStops(stopCodes.Select(s => new AtcoCode(s))).ToList();
            var distinctServiceAndOperatorCodes = stopInfo.SelectMany(x => x.Services).OrderByDescending(x => x.HasInboundOutboundDeparture).Distinct(new ServiceAndOperatorCodeComparer()).Select(x => x).ToList();
            var services = this.busService.GetServicesSummary(distinctServiceAndOperatorCodes.Distinct(), timo.ActivePeriod.StartDate, timo.ActivePeriod.EndDate);

            var serviceDetails = distinctServiceAndOperatorCodes.Where(e => e.HasDeparture).SelectMany(x =>
              {
                  var servicesByDirection = new List<ServiceByDirectionSummary>();
                  var serviceInfo = services.FirstOrDefault(e => e.ServiceCode == x.ServiceCode && e.OperatorCode == x.OperatorCode);
                  if (serviceInfo != null)
                  {
                      if (x.HasInboundDepartures)
                      {
                          this.ConvertToServiceByDirectionSummary(x, servicesByDirection, serviceInfo, new Inbound(), false);
                      }

                      if (x.HasOutboundDepartures)
                      {
                          this.ConvertToServiceByDirectionSummary(x, servicesByDirection, serviceInfo, new Outbound(), false);
                      }
                  }

                  return servicesByDirection;
              }).OrderBy(e => e.LineName.PadNumbers()).ToList();

            return this.Ok(serviceDetails);
        }

        [HttpPost]
        [Route("UpdateTimoTemplate")]
        public IActionResult UpdateTimoTemplate(string timoId, [FromBody] List<StopViewModel> stopsModel)
        {
            if (string.IsNullOrEmpty(timoId))
            {
                throw new ArgumentNullException(nameof(timoId));
            }

            var timo = this.timoService.GetTimo(timoId);
            stopsModel.ForEach(e =>
            {
                if (timo.BusStops.Any(x => x.StopCode.Equals(e.StopCode)))
                {
                    var stopCase = timo.BusStops.First(x => x.StopCode.Equals(e.StopCode)).Cases[e.StopCaseIndex];
                    stopCase.RemoveTemplate();
                    stopCase.SetTemplate(new Template(e.Template?.TemplateId, e.Template?.StyleId));
                }
            });

            this.timoService.UpdateTimo(timo);
            return this.Ok();
        }

        [HttpPost]
        [Route("UpdateServices")]
        public IActionResult UpdateServicesAndRouteGroups(string timoId, string stopCode, int stopCaseIndex, [FromBody] IEnumerable<RouteGroupViewModel> routeGroups)
        {
            if (string.IsNullOrEmpty(timoId))
            {
                throw new ArgumentNullException(nameof(timoId));
            }

            if (string.IsNullOrEmpty(stopCode))
            {
                throw new ArgumentNullException(nameof(stopCode));
            }

            var timo = this.timoService.GetTimo(timoId);
            var timoStop = timo.BusStops.First(x => x.StopCode.Equals(stopCode));

            List<TimoService> timoServices = new List<TimoService>();
            var result = routeGroups.Select(e =>
            {
                e.Services.GroupBy(s => new { s.OperatorCode, s.ServiceCode }).Select(s =>
                {
                    if (!timoServices.Exists(x => x.OperatorCode == s.Key.OperatorCode && x.ServiceCode == s.Key.ServiceCode))
                    {
                        timoServices.Add(new TimoService(s.Key.ServiceCode, s.Key.OperatorCode, s.Any(x => RouteDirection.Parse(x.Direction).IsInbound), s.Any(x => RouteDirection.Parse(x.Direction).IsOutbound)));
                    }

                    return true;
                }).ToArray();

                return new RouteGroup(
                   e.GroupName,
                   e.DisplayHeader,
                   e.Routes.Where(x => x.IsChecked).Select(x => new RouteDetail(x.ServiceCode, x.OperatorCode, x.Direction.ToString(), x.RouteRef)),
                   e.DisplayServiceHeaders,
                   e.DisableLinemaps,
                   e.DisableCompression,
                   e.TemplateStyleId,
                   e.ShowOperatingPeriodNotes);
            }).ToArray();

            var stopCase = timoStop.Cases[stopCaseIndex];
            stopCase.RemoveServices();
            stopCase.RemoveRouteGroups();
            stopCase.AddServices(timoServices);
            stopCase.AddRouteGroups(result);

            this.timoService.UpdateTimo(timo);
            bool readyToGenerate = stopCase.HasServices && (timo.HasRuleSet || stopCase.HasTemplate);

            return this.Ok(readyToGenerate);
        }

        [HttpPost]
        [Route("UpdateServicesWithDefaultRouteGroup")]
        public IActionResult UpdateServicesWithDefaultRouteGroup(string timoId, [FromBody] StopRouteGroupViewModel stopsRouteGroup)
        {
            if (string.IsNullOrEmpty(timoId))
            {
                throw new ArgumentNullException(nameof(timoId));
            }

            var timo = this.timoService.GetTimo(timoId);
            var stopsInfos = this.busStopService.GetStops(stopsRouteGroup.Stops.Select(s => new AtcoCode(s.StopCode))).ToArray();
            var allServices = this.busService.GetServices(stopsRouteGroup.Services.Select(e => new ServiceAndOperatorCode(e.ServiceCode, e.OperatorCode)).Distinct());

            foreach (var stop in stopsRouteGroup.Stops)
            {
                var stopInfo = stopsInfos.First(s => s.StopCode == stop.StopCode);
                var stopServices = stopsRouteGroup.Services.Where(s => stopInfo.Services.ToList()
                        .Exists(stopService => stopService.ServiceCode == s.ServiceCode && stopService.OperatorCode == s.OperatorCode &&
                            (stopService.InboundDirection == s.Direction || stopService.OutboundDirection == s.Direction))).ToList();

                var timoStop = timo.BusStops.First(x => x.StopCode.Equals(stop.StopCode));
                var stopCase = timoStop.Cases[stop.StopCaseIndex];
                stopCase.RemoveServices();
                stopCase.RemoveRouteGroups();

                if (!stopServices.Any())
                {
                    continue;
                }

                var stopsAllServices = allServices.Where(x => stopServices.Exists(s => s.ServiceCode == x.ServiceCode && s.OperatorCode == x.OperatorCode));
                var routes = stopsAllServices.SelectMany(e => e.GetRoutesWithNextStop(new[] { new AtcoCode(stop.StopCode) })).ToList();

                List<TimoService> timoServices = new List<TimoService>();
                var result = stopsRouteGroup.RouteGroups.Select(e =>
                {
                    List<RouteDetail> selectedRoutes = new List<RouteDetail>();
                    foreach (var route in routes)
                    {
                        selectedRoutes.AddRange(e.Routes.Where(x => x.IsChecked && x.ServiceCode == route.ServiceCode && x.OperatorCode == route.OperatorCode && x.Direction == route.Direction.ToString() && x.RouteRef == route.RouteRef).Select(x => new RouteDetail(x.ServiceCode, x.OperatorCode, x.Direction.ToString(), x.RouteRef)));
                    }

                    return new RouteGroup(
                        e.GroupName,
                        e.DisplayHeader,
                        selectedRoutes,
                        e.DisplayServiceHeaders,
                        e.DisableLinemaps,
                        e.DisableCompression,
                        e.TemplateStyleId,
                        e.ShowOperatingPeriodNotes);
                }).ToArray();

                var timoServicesData = stopServices.GroupBy(s => new { s.OperatorCode, s.ServiceCode }).Select(ser =>
                      new TimoService(ser.Key.ServiceCode, ser.Key.OperatorCode, ser.Any(x => RouteDirection.Parse(x.Direction).IsInbound), ser.Any(x => RouteDirection.Parse(x.Direction).IsOutbound))).ToArray();
                timoServices.AddRange(timoServicesData);

                var validRouteGroups = result.Where(routeGroup => routeGroup.RoutesDetail.Any()).ToList();
                stopCase.AddServices(timoServices);
                stopCase.AddRouteGroups(validRouteGroups);
            }

            this.timoService.UpdateTimo(timo);

            return this.Ok();
        }

        [Route("SplitRow")]
        public IActionResult SplitRow(string timoId, string stopCode)
        {
            if (string.IsNullOrEmpty(stopCode))
            {
                throw new ArgumentNullException(nameof(stopCode));
            }

            if (string.IsNullOrEmpty(timoId))
            {
                throw new ArgumentNullException(nameof(timoId));
            }

            var timo = this.timoService.GetTimo(timoId);
            timo.BusStops.Single(s => s.StopCode.Equals(stopCode)).AddCases(new[] { new Case() });

            this.timoService.UpdateTimo(timo);
            return this.Ok();
        }

        [Route("DeleteChildRow")]
        public IActionResult DeleteChildRow(string timoId, string stopCode, int stopCaseIndex)
        {
            if (string.IsNullOrEmpty(timoId))
            {
                throw new ArgumentNullException(nameof(timoId));
            }

            if (string.IsNullOrEmpty(stopCode))
            {
                throw new ArgumentNullException(nameof(stopCode));
            }

            var timo = this.timoService.GetTimo(timoId);
            timo.BusStops.First(e => e.StopCode.Equals(stopCode)).RemoveCase(stopCaseIndex);

            this.timoService.UpdateTimo(timo);
            return this.Ok();
        }

        [HttpPost]
        [Route("RouteGroupsForStops")]
        public IActionResult GetRouteGroupsForStops([FromBody]StopsServicesViewModel stopService, RouteGroupType routeGroupType)
        {
            var selectedServiceCodes = stopService.Services.Select(e => new ServiceAndOperatorCode(e.ServiceCode, e.OperatorCode));
            var routes = this.busService.GetRoutesWithNextStop(selectedServiceCodes.Distinct(), stopService.Stops.Select(x => new AtcoCode(x.StopCode)).ToList()).ToArray();

            IList<RouteGroup> defaultRouteGroups = new List<RouteGroup>();
            if (routeGroupType == RouteGroupType.MultiService)
            {
                defaultRouteGroups = new List<RouteGroup>() { this.GetDefaultRouteGroup(stopService.Services, routes.ToList(), new RouteGroupViewModel()) };
            }
            else
            {
                defaultRouteGroups = stopService.Services.Select(x => this.GetDefaultRouteGroup(new List<ServiceByDirectionSummary>() { x }, routes.ToList(), new RouteGroupViewModel())).ToArray();
            }

            var routesForSelectedServices = stopService.Services.SelectMany(y =>
                routes.Where(e => e.OperatorCode == y.OperatorCode && e.ServiceCode == y.ServiceCode && e.Direction.ToString() == y.Direction));

            var routeGroups = defaultRouteGroups.Select(e =>
            {
                var routesModel = routesForSelectedServices.Select(x => new RouteDestinationViewModel
                {
                    ServiceCode = x.ServiceCode,
                    JourneyCount = x.JourneyCount,
                    RouteRef = x.RouteRef,
                    Destination = x.Description,
                    IsChecked = e.RoutesDetail.Select(r => r).Any(r => r.RouteRef == x.RouteRef && r.ServiceCode == x.ServiceCode && r.OperatorCode == x.OperatorCode && r.Direction == x.Direction.ToString()),
                    LineName = stopService.Services.First(l => l.ServiceCode == x.ServiceCode && l.OperatorCode == x.OperatorCode).LineName,
                    JourneyPatternSectionRefs = x.JourneyPatternSections.Select(j => j.JourneyPatternSectionRef).ToArray(),
                    Direction = x.Direction.ToString(),
                    Description = stopService.Services.First(l => l.OperatorCode == x.OperatorCode && l.ServiceCode == x.ServiceCode && l.Direction == x.Direction.ToString()).Description,
                    OperatorCode = x.OperatorCode
                }).OrderBy(x => x.LineName.PadNumbers()).ToList();

                return new RouteGroupViewModel(routesModel, e.GroupName, e.DisplayHeader, e.DisplayServiceHeaders, e.DisableLinemaps, e.DisableCompression, e.TemplateStyleId, e.ShowOperatingPeriodNotes);
            }).ToList();

            return this.Ok(new RouteGroupsWithStatusViewModel(routeGroups, true));
        }

        [HttpPost]
        [Route("RouteGroups")]
        public IActionResult GetRouteGroups([FromBody]List<ServiceByDirectionSummary> services, string timoId, string stopCode, int stopCaseIndex)
        {
            var timo = this.timoService.GetTimo(timoId);
            var timoCase = timo.BusStops.Single(e => e.StopCode.Value == stopCode).Cases[stopCaseIndex];
            var timoRouteGroups = timoCase.RouteGroups;

            var selectedServiceCodes = services.Select(e => new ServiceAndOperatorCode(e.ServiceCode, e.OperatorCode))
                .Union(timoCase.Services.Select(e => new ServiceAndOperatorCode(e.ServiceCode, e.OperatorCode)));

            var routes = this.busService.GetRoutesWithNextStop(selectedServiceCodes, new[] { new AtcoCode(stopCode) }).ToArray();

            if (!timoRouteGroups.Any())
            {
                timoRouteGroups = services.Select(x => this.GetDefaultRouteGroup(new List<ServiceByDirectionSummary>() { x }, routes.ToList(), new RouteGroupViewModel())).ToArray();
            }

            var routesForSelectedServices = services.SelectMany(y =>
                routes.Where(e => e.OperatorCode == y.OperatorCode && e.ServiceCode == y.ServiceCode && e.Direction.ToString() == y.Direction));

            var allRouteIdsExist = this.IsRouteDataChanged(timoRouteGroups.SelectMany(e => e.RoutesDetail.Select(r => r.RouteRef)), routes.Select(e => e.RouteRef));

            var routeGroups = timoRouteGroups.Select(e =>
            {
                var routesModel = routesForSelectedServices.Select(x => new RouteDestinationViewModel
                {
                    ServiceCode = x.ServiceCode,
                    JourneyCount = x.JourneyCount,
                    RouteRef = x.RouteRef,
                    Destination = x.Description,
                    IsChecked = e.RoutesDetail.Select(r => r).Any(r => r.RouteRef == x.RouteRef && r.ServiceCode == x.ServiceCode && r.OperatorCode == x.OperatorCode && r.Direction == x.Direction.ToString()),
                    LineName = services.First(l => l.ServiceCode == x.ServiceCode && l.OperatorCode == x.OperatorCode).LineName,
                    JourneyPatternSectionRefs = x.JourneyPatternSections.Select(j => j.JourneyPatternSectionRef).ToArray(),
                    Direction = x.Direction.ToString(),
                    Description = services.First(l => l.OperatorCode == x.OperatorCode && l.ServiceCode == x.ServiceCode && l.Direction == x.Direction.ToString()).Description,
                    OperatorCode = x.OperatorCode
                }).OrderBy(x => x.LineName.PadNumbers()).ToList();

                return new RouteGroupViewModel(routesModel, e.GroupName, e.DisplayHeader, e.DisplayServiceHeaders, e.DisableLinemaps, e.DisableCompression, e.TemplateStyleId, e.ShowOperatingPeriodNotes);
            }).ToList();

            return this.Ok(new RouteGroupsWithStatusViewModel(routeGroups, allRouteIdsExist));
        }

        [Route("Preview")]
        public async Task<IActionResult> PreviewTimetable(string stopCode, int stopIndex, string timoId)
        {
            if (this.client.IsWebApiRunning)
            {
                var response = await this.client.PreviewTimeTableAsync(stopCode, stopIndex, timoId, this.authenticationProvider().CustomerId);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsAsync<PreviewResult>();
                    return this.Ok(result);
                }

                return this.NoContent();
            }

            return this.NotFound(new { message = "Timetable service is not running" });
        }

        [HttpPost]
        [Route("RemoveStops")]
        public IActionResult RemoveStops([FromBody] List<StopViewModel> stops, string timoId)
        {
            if (stops == null)
            {
                throw new ArgumentNullException(nameof(stops));
            }

            if (string.IsNullOrEmpty(timoId))
            {
                throw new ArgumentNullException(nameof(timoId));
            }

            var stopList = stops.Select(x => new StopCaseIdentifier(x.StopCode, x.StopCaseIndex)).ToList();
            this.timoService.RemoveStops(stopList, timoId);

            return this.Ok();
        }

        [HttpPost]
        [Route("UpdateStopTags")]
        public IActionResult UpdateStopTags(string timoId, [FromBody] List<StopViewModel> stopsModel)
        {
            if (string.IsNullOrEmpty(timoId))
            {
                throw new ArgumentNullException(nameof(timoId));
            }

            var timo = this.timoService.GetTimo(timoId);
            stopsModel.ForEach(e =>
            {
                var stopCase = timo.BusStops.First(x => x.StopCode.Equals(e.StopCode)).Cases[e.StopCaseIndex];
                stopCase.SetTags(e.Tags);
            });

            this.timoService.UpdateTimo(timo);
            return this.Ok();
        }

        [HttpPost]
        [Route("ResetAllConfigurations")]
        public IActionResult ResetAllConfigurations(string timoId, [FromBody] List<StopViewModel> stopsModel)
        {
            if (string.IsNullOrEmpty(timoId))
            {
                throw new ArgumentNullException(nameof(timoId));
            }

            var timo = this.timoService.GetTimo(timoId);
            stopsModel.ForEach(e =>
            {
                if (timo.BusStops.Any(x => x.StopCode.Equals(e.StopCode)))
                {
                    var stopCase = timo.BusStops.First(x => x.StopCode.Equals(e.StopCode)).Cases[e.StopCaseIndex];
                    stopCase.RemoveRouteGroups();
                    stopCase.RemoveServices();
                    stopCase.ResetTemplate();
                    stopCase.RemoveTags();
                }
            });

            this.timoService.UpdateTimo(timo);
            return this.Ok();
        }

        [Route("GetStopSummary")]
        public IActionResult GetStopSummary(string stopCode)
        {
            if (string.IsNullOrEmpty(stopCode))
            {
                throw new ArgumentNullException(nameof(stopCode));
            }

            var location = this.busStopService.GetStopLocation(new AtcoCode(stopCode));

            var stop = this.busStopService.GetStops(new[] { new AtcoCode(stopCode) }).First();
            var services = this.busService.GetServicesSummary(stop.Services).Distinct();

            var stopServices = stop.Services.Where(x => x.HasDeparture).Select(x =>
            {
                var serviceInfo = services.First(s => s.OperatorCode == x.OperatorCode && s.ServiceCode == x.ServiceCode);
                return new TimoServiceViewModel(x.ServiceCode, x.HasInboundDepartures, x.HasOutboundDepartures, serviceInfo.LineName, x.OperatorCode, serviceInfo.OperatorName);
            }).OrderBy(x => x.LineName.PadNumbers());

            return this.Ok(new { Location = location, Services = stopServices });
        }

        private List<RouteGroupViewModel> GetRouteGroupsWithServices(List<ServiceByDirectionSummary> stopServices, IList<RouteGroup> defaultRouteGroups, IEnumerable<Route> routesForSelectedServices)
        {
            return defaultRouteGroups.Select(e =>
            {
                var routesModel = routesForSelectedServices.Select(x => new RouteDestinationViewModel
                {
                    ServiceCode = x.ServiceCode,
                    JourneyCount = x.JourneyCount,
                    RouteRef = x.RouteRef,
                    Destination = x.Description,
                    IsChecked = e.RoutesDetail.Select(r => r).Any(r => r.RouteRef == x.RouteRef && r.ServiceCode == x.ServiceCode && r.OperatorCode == x.OperatorCode && r.Direction == x.Direction.ToString()),
                    LineName = stopServices.First(l => l.ServiceCode == x.ServiceCode && l.OperatorCode == x.OperatorCode).LineName,
                    JourneyPatternSectionRefs = x.JourneyPatternSections.Select(j => j.JourneyPatternSectionRef).ToArray(),
                    Direction = x.Direction.ToString(),
                    Description = stopServices.First(l => l.OperatorCode == x.OperatorCode && l.ServiceCode == x.ServiceCode && l.Direction == x.Direction.ToString()).Description,
                    OperatorCode = x.OperatorCode
                }).ToList();

                var routeGroupData = new RouteGroupViewModel(routesModel, e.GroupName, e.DisplayHeader, e.DisplayServiceHeaders, e.DisableLinemaps, e.DisableCompression, e.TemplateStyleId, e.ShowOperatingPeriodNotes);
                routeGroupData.Services = stopServices.Where(s => routesModel.Exists(x => x.ServiceCode == s.ServiceCode && x.OperatorCode == s.OperatorCode && x.Direction == s.Direction.ToString())).ToList();

                return routeGroupData;
            }).ToList();
        }

        private RouteGroup GetDefaultRouteGroup(List<ServiceByDirectionSummary> services, List<Route> routes, RouteGroupViewModel routeGroup)
        {
            return new RouteGroup(
                routeGroup.GroupName ?? string.Empty,
                routeGroup.DisplayHeader,
                routes.Where(y => services.Any(service => service.OperatorCode == y.OperatorCode && y.ServiceCode == service.ServiceCode && y.Direction.ToString() == service.Direction)).Select(z => new RouteDetail(z.ServiceCode, z.OperatorCode, z.Direction.ToString(), z.RouteRef)).ToArray(),
                true,
                routeGroup.DisableLinemaps,
                routeGroup.DisableCompression,
                routeGroup.TemplateStyleId ?? string.Empty,
                routeGroup.ShowOperatingPeriodNotes);
        }

        private void ConvertToServiceByDirectionSummary(ServiceAndOperatorCode serviceAndOperatorCode, List<ServiceByDirectionSummary> servicesByDirection, BusServiceSummary serviceInfo, RouteDirection direction, bool selected)
        {
            servicesByDirection.Add(new ServiceByDirectionSummary(
                serviceAndOperatorCode.ServiceCode,
                serviceInfo.LineName,
                direction.ToString(),
                serviceInfo.Description.ForDirection(direction),
                selected,
                serviceAndOperatorCode.OperatorCode,
                serviceInfo.OperatorName));
        }

        private bool IsRouteDataChanged(IEnumerable<string> timoRouteRefs, IEnumerable<string> selectedServicesRouteRefs)
        {
            return timoRouteRefs.All(e => selectedServicesRouteRefs.Contains(e));
        }
    }
}