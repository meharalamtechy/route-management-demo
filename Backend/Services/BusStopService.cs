using Mission.Crosstown.Model;
using Mission.TimeTable.Domain.Interfaces;
using Mission.TimeTable.Domain.Interfaces.Services;
using Mission.TimeTable.Domain.Model;
using Mission.TimeTable.Domain.Model.TimeTable;
using Mission.TimeTable.Domain.ViewModel;
using System.Collections.Generic;
using System.Linq;

namespace Mission.TimeTable.Domain.Services
{
    public class BusStopService : IBusStopService
    {
        private readonly IBusStopRepository busStopRepository;
        private readonly IBusStopAmendmentRepository busStopAmendmentRepository;

        public BusStopService(IBusStopRepository busStopRepository, IBusStopAmendmentRepository busStopAmendmentRepository)
        {
            this.busStopRepository = busStopRepository;
            this.busStopAmendmentRepository = busStopAmendmentRepository;
        }

        public IList<ValidationResult<BusStopSummary>> ValidateStops(IEnumerable<string> stopCodes)
        {
            var existingStops = this.busStopRepository.GetValidStops(stopCodes);
            return stopCodes.Select(code =>
               {
                   var stop = existingStops.FirstOrDefault(e => e.StopCode.Equals(code) || e.NaptanCode == code);
                   if (stop != null)
                   {
                       return new ValidationResult<BusStopSummary>(new BusStopSummary(stop.StopCode, stop.StopName, stop.NaptanCode, new NullSize()), true);
                   }

                   return new ValidationResult<BusStopSummary>(new BusStopSummary(code, string.Empty, string.Empty), false);
               })
               .ToList();
        }

        public IList<Stop> GetStops(IEnumerable<AtcoCode> stopCodes)
        {
            var stops = this.busStopRepository.GetStops(stopCodes.Select(e => e.Value));
            this.ApplyStopAmendments(stops);
            return stops;
        }

        public IList<Stop> GetStops(IEnumerable<BatchLogDetail> batchDetails)
        {
            List<Stop> result = new List<Stop>();
            var stops = this.busStopRepository.GetStops(batchDetails.Select(e => e.StopCode.ToString()).Distinct());
            this.ApplyStopAmendments(stops);

            foreach (var batchDetail in batchDetails)
            {
                var stop = stops.First(x => x.StopCode == batchDetail.StopCode);
                var stopInfo = (Stop)stop.Clone();
                stopInfo.SetCaseInformation(0, batchDetail.PrintSize);
                result.Add(stopInfo);
            }

            return result;
        }

        public IList<Stop> OrderStopsByRoutePosition(IEnumerable<AtcoCode> orderedStopCodes)
        {
            var stops = this.busStopRepository.GetStops(orderedStopCodes.Select(x => x.Value));
            this.ApplyStopAmendments(stops);
            var allStops = orderedStopCodes.Where(stopCode => stops.Any(e => e.StopCode == stopCode)).Select(e => stops.Single(x => x.StopCode == e).Clone() as Stop).ToList();
            var result = allStops.GroupBy(e => e.StopCode).SelectMany((stopsFilter, index1) =>
            {
                int index = 0;
                foreach (var item in stopsFilter.ToList())
                {
                    item.SetIndex(index);
                    index++;
                }

                return stopsFilter;
            }).ToList();            return allStops;        }

        public IList<Stop> OrderStopsByRoutePosition(IEnumerable<StopsAndTimingStatuses> orderedStops)
        {
            var stops = this.busStopRepository.GetStops(orderedStops.Select(x => x.StopCode.Value));
            this.ApplyStopAmendments(stops);
            return orderedStops.Where(stop => stops.Any(e => e.StopCode == stop.StopCode)).Select(e =>
            {
                var stop = stops.Single(x => x.StopCode == e.StopCode);
                stop.SetIndex(e.StopIndex);
                return stop;
            }).ToList();
        }

        public void SaveStops(IEnumerable<Stop> stopsModel)
        {
            this.busStopRepository.SaveNaptanData(stopsModel);
        }

        public int GetNumberOfStops(ServiceAndOperatorCode service)
        {
            return this.busStopRepository.GetNumberOfStops(service);
        }

        public Location GetStopLocation(AtcoCode atcoCode)
        {
            return this.busStopRepository.GetStopLocation(atcoCode);
        }

        public string GetStopName(string stopCode)
        {
            return this.busStopRepository.GetStopName(stopCode);
        }

        public IList<Stop> GetStopsByStopCodesIfExist(IEnumerable<AtcoCode> stopCodes)
        {
            var stops = this.busStopRepository.GetStopsByStopCodesIfExist(stopCodes.Select(e => e.Value));
            this.ApplyStopAmendments(stops);

            var result = stops.GroupBy(e => e.StopCode);
            foreach (var stopsFilter in result)
            {
                int index = 0;
                foreach (var item in stopsFilter)
                {
                    item.SetIndex(index);
                    index++;
                }
            }

            return stops;
        }

        private void ApplyStopAmendments(IEnumerable<Stop> stops)
        {
            foreach (var stop in stops)
            {
                stop.SetDefaultStopAndRoadName(stop.StopName, stop.Road, stop.LocalityName);
            }

            var stopsAmendments = this.busStopAmendmentRepository.GetBusStopAmendments(stops.Select(e => e.StopCode.Value));
            foreach (var amendment in stopsAmendments)
            {
                var stop = stops.FirstOrDefault(e => e.StopCode == amendment.StopCode);
                amendment.ApplyChanges(stop);
            }
        }
    }
}
