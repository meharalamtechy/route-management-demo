using System;
using System.Collections.Generic;
using System.Linq;
using Mission.Crosstown.Model;
using Mission.TimeTable.DAL.Entities.TimeTable;
using Mission.TimeTable.DAL.Interfaces;
using Mission.TimeTable.Domain.Interfaces;
using Mission.TimeTable.Domain.Model;
using Mission.TimeTable.Domain.Model.TimeTable;
using Mission.TimeTable.Domain.Model.TimeTable.Routes;
using MongoDB.Bson;

namespace Mission.TimeTable.Domain.Repositories
{
    public class TimoRepository : ITimoRepository
    {
        private readonly IEntityCollectionProvider<TimoData> timoProvider;

        public TimoRepository(IEntityCollectionProvider<TimoData> timoProvider)
        {
            this.timoProvider = timoProvider;
        }

        public Timo GetTimo(ObjectId timoId)
        {
            var timoData = this.timoProvider.GetAll().First(e => e.Id == timoId);
            return this.CreateTimoObject(timoData);
        }

        public IList<Timo> GetTimos()
        {
            var timosData = this.timoProvider.GetAll().Where(x => !x.IsDeleted && !x.IsCompleted).OrderByDescending(e => e.TimoDate).ToList();
            return this.ConvertToTimoModel(timosData);
        }

        public IList<Timo> GetTimos(IEnumerable<string> timoIds)
        {
            var timoData = this.timoProvider.GetAll(true)
                .OrderByDescending(e => e.TimoDate)
                .ToList();

            return this.ConvertToTimoModel(timoData
                .Where(x => timoIds.Contains(x.Id.ToString()))
                .ToList());
        }

        public IList<Timo> GetCompletedTimos()
        {
            var timosData = this.timoProvider.GetAll().Where(x => !x.IsDeleted && x.IsCompleted).OrderByDescending(e => e.CompletedDate).ToList();
            return this.ConvertToTimoModel(timosData);
        }

        public IList<Timo> GetTimosByStopCode(string stopCode)
        {
            var timosData = this.timoProvider.GetAll().Where(x => !x.IsDeleted && x.BusStops.Any(s => s.StopCode == stopCode)).OrderByDescending(e => e.TimoDate).ToList();
            return this.ConvertToTimoModel(timosData);
        }

        public string UpsertTimo(Timo timo)
        {
            if (timo == null)
            {
                throw new ArgumentNullException(nameof(timo));
            }

            var timoBusStops = timo.BusStops.Select(e =>
            {
                double? caseHeight = null;
                double? caseWidth = null;
                if (e.Size.HasValue)
                {
                    caseHeight = e.Size.Height;
                    caseWidth = e.Size.Width;
                }

                return new TimoBusStopData(
                    e.StopCode.ToString(),
                    caseWidth,
                    caseHeight,
                    e.CaseQuantity,
                    e.Cases.Select(x => new CaseData(
                        new TemplateData(x.Template.Id, x.Template.StyleId),
                        x.Services.Select(s => new TimoServiceData(
                            s.ServiceCode,
                            s.OperatorCode,
                            s.Inbound,
                            s.Outbound)),
                        x.RouteGroups.Select(r => new RouteGroupData(
                            r.GroupName,
                            r.DisplayHeader,
                            r.RoutesDetail.Select(rd => new RouteDetailData(rd.ServiceCode, rd.OperatorCode, rd.Direction, rd.RouteRef)),
                            r.DisplayServiceHeaders,
                            r.DisableLinemaps,
                            r.DisableCompression,
                            r.TemplateStyleId,
                            r.ShowOperatingPeriodNotes)),
                        x.Tags)));
            });

            TimoData timoData = new TimoData(
                DocumentId.ToObjectId(timo.TimoID),
                timo.TimoName,
                string.IsNullOrEmpty(timo.TimoID) ? DateTime.Now : timo.TimoDate,
                timo.CompletedDate,
                timoBusStops,
                timo.ActivePeriod.StartDate,
                timo.ActivePeriod.EndDate,
                timo.IsDeleted,
                timo.IsCompleted,
                timo.RuleSetId,
                timo.TimoAddInfo);

            this.timoProvider.Upsert(timoData);
            return timoData.Id.ToString();
        }

        public List<StopSummary> GetTimosCount(List<AtcoCode> atcoCodes)
        {
            var stopCodes = atcoCodes.Select(e => e.Value).ToArray();
            var timoStopSummary = this.timoProvider.GetAll(false).SelectMany(x => x.BusStops).Select(x => x.StopCode).Where(t => stopCodes.Contains(t)).GroupBy(x => x).Select(x => new StopSummary(x.Key, x.Count())).ToList();
            return atcoCodes.Select(e =>
            {
                var stopInfo = timoStopSummary.FirstOrDefault(z => z.StopCode == e.Value);
                if (stopInfo == null)
                {
                    return new StopSummary(e.Value, 0);
                }

                return stopInfo;
            }).ToList();
        }

        private Timo CreateTimoObject(TimoData timoData)
        {
            Timo timo = new Timo(timoData.Id.ToString(), timoData.TimoDate, timoData.CompletedDate, timoData.TimoName, timoData.TimoAddInfo, new Period(timoData.StartPeriod, timoData.EndPeriod), timoData.IsCompleted);
            timo.SetRuleSet(timoData.RuleSetId);

            timoData.BusStops.ToList().ForEach(e =>
            {
                Size caseSize = new NullSize();
                if (e.CaseWidth.HasValue || e.CaseHeight.HasValue)
                {
                    caseSize = new Size(e.CaseWidth.Value, e.CaseHeight.Value);
                }

                TimoBusStop busStop = new TimoBusStop(new AtcoCode(e.StopCode), caseSize, e.CaseQuantity);
                busStop.AddCases(e.Cases.Select(x =>
                {
                    var routeGroups = x.RouteGroups.Select(r => new RouteGroup(
                        r.GroupName,
                        r.DisplayHeader,
                        r.RoutesDetail.Select(rd => new RouteDetail(rd.ServiceCode, rd.OperatorCode, rd.Direction, rd.RouteRef)),
                        r.DisplayServiceHeaders,
                        r.DisableLinemaps,
                        r.DisableCompression,
                        r.TemplateStyleId,
                        r.ShowOperatingPeriodNotes)).ToList();

                    Case stopCase = new Case(
                        x.Services.Select(s => new TimoService(s.ServiceCode, s.OperatorCode, s.Inbound, s.Outbound)).ToList(),
                        routeGroups,
                        new Template(x.Template.Id, x.Template.StyleId),
                        x.Tags != null ? x.Tags.ToList() : null);
                    return stopCase;
                }));

                timo.AddBusStops(new[] { busStop });
            });

            return timo;
        }

        private IList<Timo> ConvertToTimoModel(List<TimoData> timosData)
        {
            return timosData.Select(x => this.CreateTimoObject(x)).ToList();
        }
    }
}