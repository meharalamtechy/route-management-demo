using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Mission.Crosstown.Model;
using Mission.TimeTable.DAL.Interfaces;
using Mission.TimeTable.DAL.Utility;
using Mission.TimeTable.Domain.Interfaces.Services;
using Mission.TimeTable.Domain.Model;
using Mission.TimeTable.Domain.Model.TimeTable;
using Mission.TimeTable.Domain.Utility;
using Mission.TimeTable.Model.TimeTable;
using Mission.TimeTable.Web.Filters;
using Mission.TimeTable.Web.Model.Account;
using Mission.TimeTable.Web.Model.TimeTable;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Mission.TimeTable.Web.Controllers
{
    [Route("api/stopmanager")]
    [Authorize(Policy = "RoleVerification", Roles = UserRole.Admin + " , " + UserRole.Standard)]
    [ServiceFilter(typeof(CustomerDatabaseFilter))]
    public class StopManagerController : Controller
    {
        private readonly IBusServiceService busService;
        private readonly IBusStopService busStopService;
        private readonly IBusStopAmendmentService busStopAmendmentService;
        private readonly ITimoService timoService;
        private readonly TimeTableSettings timetableSettings;
        private readonly IAuthenticationProvider authenticationProvider;
        private readonly IStopImageService stopImageService;

        public StopManagerController(
            IBusServiceService busService,
            IBusStopService busStopService,
            IBusStopAmendmentService busStopAmendmentService,
            ITimoService timoService,
            TimeTableSettings timetableSettings,
            IAuthenticationProvider authenticationProvider,
            IStopImageService stopImageService)
        {
            this.busService = busService;
            this.busStopService = busStopService;
            this.busStopAmendmentService = busStopAmendmentService;
            this.timoService = timoService;
            this.timetableSettings = timetableSettings;
            this.authenticationProvider = authenticationProvider;
            this.stopImageService = stopImageService;
        }

        [Route("GetStopsByServices")]
        public IActionResult GetStopsByServices([FromBody] BusServiceSummaryViewModel service)
        {
            var routesModel = this.busService.GetRoutes(new List<ServiceAndOperatorCode> { new ServiceAndOperatorCode(service.ServiceCode, service.OperatorCode) });
            var uniqueStopCodes = routesModel.SelectMany(x => x.FromStopCodes).Distinct().ToList();
            var stops = this.busStopService.OrderStopsByRoutePosition(uniqueStopCodes);
            var services = this.busService.GetServicesSummary(stops.SelectMany(e => e.Services).Distinct());
            var stopsSummary = this.timoService.GetTimosCount(uniqueStopCodes);

            string stopImagePath = Path.Combine(this.timetableSettings.StopImagePath, this.authenticationProvider.CustomerId);

            var stopsModel = stops.Select(e =>
            {
                var stopServices = e.Services.Where(x => x.HasDeparture).Select(x =>
                {
                    var serviceInfo = services.First(s => s.OperatorCode == x.OperatorCode && s.ServiceCode == x.ServiceCode);
                    return new TimoServiceViewModel(x.ServiceCode, x.HasInboundDepartures, x.HasOutboundDepartures, serviceInfo.LineName, x.OperatorCode, serviceInfo.OperatorName);
                }).OrderBy(x => x.LineName.PadNumbers());

                var stopInformation = new StopInformationViewModel(
                    e.StopCode.Value,
                    e.StopName,
                    e.NaptanCode,
                    e.Location.Bearing,
                    e.Road,
                    e.LocalityName,
                    e.Indicator,
                    e.DefaultStopName,
                    e.DefaultRoadName,
                    e.DefaultLocalityName,
                    e.FileName,
                    e.OriginalFileName,
                    stopImagePath);

                stopInformation.Services = stopServices;
                stopInformation.AssociatedTimosCount = stopsSummary.First(x => x.StopCode == e.StopCode).AssociatedTimosCount;

                return stopInformation;
            }).ToList();

            return this.Ok(stopsModel);
        }

        [HttpPost]
        [Route("UpdateBusStopAmendment")]
        public IActionResult BusStopAmendment(string stopCode, string stopAmendment, string roadAmendment, string localityAmendment)
        {
            if (string.IsNullOrEmpty(stopCode))
            {
                throw new ArgumentNullException(nameof(stopCode));
            }

            BusStopAmendment busStopAmendment = new BusStopAmendment(string.Empty, new AtcoCode(stopCode), stopAmendment, roadAmendment, localityAmendment, string.Empty, string.Empty);
            this.busStopAmendmentService.SaveBusStopAmendment(busStopAmendment);
            return this.Ok();
        }

        [HttpPut]
        [Route("SaveStopImage")]
        public IActionResult SaveStopImage(IFormFile file, string stopCode)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (string.IsNullOrEmpty(stopCode))
            {
                throw new ArgumentNullException(nameof(stopCode));
            }

            string extension = Path.GetExtension(file.FileName);
            string directoryPath = Path.Combine(this.timetableSettings.StopImagePath, this.authenticationProvider.CustomerId);
            string fileName = stopCode;
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            else
            {
                this.stopImageService.DeleteStopImage(fileName, directoryPath);
            }

            string imagePath = Path.Combine(directoryPath, fileName + extension);
            using (FileStream fs = System.IO.File.Create(imagePath))
            {
                file.CopyTo(fs);
                fs.Flush();
            }

            var busStopAmendment = this.busStopAmendmentService.GetBusStopAmendment(stopCode);
            if (busStopAmendment != null)
            {
                busStopAmendment.UpdateStopImage(fileName, file.FileName);
            }
            else
            {
                busStopAmendment = new BusStopAmendment(string.Empty, stopCode, string.Empty, string.Empty, string.Empty, fileName, file.FileName);
            }

            this.busStopAmendmentService.SaveBusStopAmendment(busStopAmendment);

            var stopInformation = new StopInformationViewModel(
                busStopAmendment.StopCode.Value,
                busStopAmendment.StopName,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                busStopAmendment.FileName,
                busStopAmendment.OriginalFileName,
                directoryPath);

            return this.Ok(stopInformation);
        }

        [HttpDelete]
        [Route("DeleteStopImage")]
        public IActionResult DeleteStopImage(string stopCode)
        {
            if (string.IsNullOrEmpty(stopCode))
            {
                throw new ArgumentNullException(nameof(stopCode));
            }

            string directoryPath = Path.Combine(this.timetableSettings.StopImagePath, this.authenticationProvider.CustomerId);
            string fileName = stopCode;
            this.stopImageService.DeleteStopImage(fileName, directoryPath);
            this.busStopAmendmentService.DeleteStopImage(stopCode);

            return this.Ok();
        }
    }
}
