using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mission.TimeTable.DAL.Entities;
using Mission.TimeTable.DAL.Entities.TimeTable;
using Mission.TimeTable.Domain.Exceptions;
using Mission.TimeTable.Domain.Interfaces;
using Mission.TimeTable.Domain.Interfaces.Services;
using Mission.TimeTable.Domain.Model;
using Mission.TimeTable.Domain.Model.TimeTable.Routes;
using Mission.TimeTable.Domain.Utility;
using Mission.TimeTable.Domain.Utility.Constants;
using Mission.TimeTable.Domain.ViewModel;
using Mission.TimeTable.Logger.Model;
using Matrix = Mission.TimeTable.Domain.Model.Matrix.Matrix;
using MatrixLogger = Mission.TimeTable.Logger.Model.MatrixLogger;
using MatrixRouteGroupService = Mission.TimeTable.Domain.Model.Matrix.MatrixRouteGroupService;

namespace Mission.TimeTable.Domain.Services
{
    public class MatrixService : IMatrixService
    {
        private readonly IMatrixRepository matrixRepository;
        private readonly IBusServiceService busService;
        private readonly TimeTableSettings settings;

        public MatrixService(IMatrixRepository matrixRepository, IBusServiceService busService, TimeTableSettings settings)
        {
            this.matrixRepository = matrixRepository;
            this.busService = busService;
            this.settings = settings;
        }

        public IList<Matrix> GetMatrices()
        {
            return this.matrixRepository.GetMatrices();
        }

        public string SaveMatrix(Matrix matrix)
        {
            MatrixData matrixData = this.BindMatrixData(matrix);

            return this.matrixRepository.UpsertMatrix(matrixData).Id.ToString();
        }

        public void UpdateMatrix(Matrix matrix)
        {
            if (matrix == null)
            {
                throw new ArgumentNullException(nameof(ArgumentNullException));
            }

            var matrixData = this.BindMatrixData(matrix);
            this.matrixRepository.UpsertMatrix(matrixData);
        }

        public MatrixLogger.MatrixGenerationStatus MatrixTimeTableGenerated(string matrixId)
        {
            string matrixPath = Path.Combine(this.settings.MatrixTimetableUrl, matrixId);
            var matrixLogs = this.GetMatrixGenerationLogs(matrixId);
            var isFileGenerated = (Directory.Exists(matrixPath) && Directory.GetFiles(matrixPath).Any(x => x.EndsWith(FileExtensions.Pdf))) || matrixLogs.Count > 0;
            return new MatrixLogger.MatrixGenerationStatus(isFileGenerated, matrixLogs);
        }

        public bool IsMatrixGenerationInProgress(string matrixId)
        {
            string matrixPath = Path.Combine(this.settings.MatrixTimetableUrl, matrixId);
            var matrixLogsCount = this.GetMatrixGenerationLogs(matrixId).Count();
            var isFileGenerationInProgress = Directory.Exists(matrixPath) && !Directory.GetFiles(matrixPath).Any(x => x.EndsWith(FileExtensions.Pdf)) && matrixLogsCount == 0;
            return isFileGenerationInProgress;
        }

        public Matrix GetMatrix(string matrixId)
        {
            var matrix = this.matrixRepository.GetMatrix(DocumentId.ToObjectId(matrixId));
            if (matrix == null)
            {
                throw new RecordNotFoundException($"'{nameof(matrixId)} = {matrixId}'");
            }

            return matrix;
        }

        public void ChangeServiceDirection(string matrixId, string serviceCode, string operatorCode, int routeGroupIndex, bool inbound = true)
        {
            var matrix = this.GetMatrix(matrixId);
            var routes = this.busService.GetRoutes(new[] { new ServiceAndOperatorCode(serviceCode, operatorCode) });
            var routeGroup = matrix.RouteGroups.ElementAt(routeGroupIndex);
            var direction = RouteDirection.Parse(inbound ? "Inbound" : "Outbound");

            routeGroup.SelectedServices.First(x => x.ServiceCode == serviceCode).ChangeServiceDirection(inbound, !inbound, routes.Select(x => x.RouteRef).ToList());

            if (routeGroup.SelectedServices.Count() == 1)
            {
                var mainRoute = routes.Where(x => x.Direction == direction).OrderByDescending(x => x.JourneyCount).FirstOrDefault();
                routeGroup.UpdateStops(mainRoute != null ? mainRoute.StopsAndTimingStatuses.Where(x => x.PrimaryTimingStatus == TimingStatuses.Ptp).Select(x => new StopAndArrival(x.StopCode, false, 0)).ToList() : new List<StopAndArrival>());
            }

            this.matrixRepository.UpsertMatrix(this.BindMatrixData(matrix));
        }

        public void UpdateMatrixService(string matrixId, List<string> routes)
        {
            var matrix = this.GetMatrix(matrixId);
            var matrixRouteGroupService = matrix.RouteGroups.First().SelectedServices.First();

            matrix.RouteGroups.First().UpdateServices(new[] { new MatrixRouteGroupService(matrixRouteGroupService.ServiceCode, matrixRouteGroupService.ServiceDisplayText, matrixRouteGroupService.Inbound, matrixRouteGroupService.Outbound, matrixRouteGroupService.OperatorCode, matrixRouteGroupService.OperatorDisplayText, routes) });

            var matrixData = this.BindMatrixData(matrix);
            this.matrixRepository.UpsertMatrix(matrixData);
        }

        public void UpdateMatrixStops(string matrixId, int routeGroupIndex, List<StopAndArrival> stops)
        {
            var matrix = this.GetMatrix(matrixId);
            matrix.RouteGroups.ElementAt(routeGroupIndex).UpdateStops(stops);

            var matrixData = this.BindMatrixData(matrix);
            this.matrixRepository.UpsertMatrix(matrixData);
        }

        public void UpdateMatrixStops(string matrixId, List<StopAndArrival> stops, int routeGroupIndex)
        {
            var matrix = this.GetMatrix(matrixId);
            matrix.RouteGroups.ElementAt(routeGroupIndex).UpdateStops(stops);

            var matrixData = this.BindMatrixData(matrix);
            this.matrixRepository.UpsertMatrix(matrixData);
        }

        public void UpdateKeyLocations(string matrixId, List<string> locations)
        {
            var matrix = this.GetMatrix(matrixId);
            matrix.UpdateKeyLocations(locations);

            var matrixData = this.BindMatrixData(matrix);
            this.matrixRepository.UpsertMatrix(matrixData);
        }

        public void SaveMatrixServices(string matrixId, int routeGroupIndex, List<MatrixRouteGroupService> servicesModel)
        {
            var matrix = this.GetMatrix(matrixId);
            var routeGroup = matrix.RouteGroups.ElementAt(routeGroupIndex);
            var routeMatrixServices = routeGroup.SelectedServices;
            List<MatrixRouteGroupService> matrixServices = new List<MatrixRouteGroupService>();
            List<StopsAndTimingStatuses> routePtpStops = new List<StopsAndTimingStatuses>();

            foreach (var serviceModel in servicesModel)
            {
                var existingService = routeMatrixServices.FirstOrDefault(x => x.ServiceCode == serviceModel.ServiceCode);
                List<Route> selectedRoutes = new List<Route>();

                if (existingService == null)
                {
                    var serviceAndOperatorCode = new ServiceAndOperatorCode(serviceModel.ServiceCode, serviceModel.OperatorCode);
                    selectedRoutes = this.busService.GetRoutes(new[] { serviceAndOperatorCode }).Where(e => e.Direction.IsInbound).ToList();
                    var serviceSummary = this.busService.GetServicesSummary(new[] { serviceAndOperatorCode }).Single();
                    matrixServices.Add(new MatrixRouteGroupService(
                        serviceModel.ServiceCode,
                        string.IsNullOrEmpty(serviceSummary.DisplayText) ? serviceSummary.LineName : serviceSummary.DisplayText,
                        true,
                        false,
                        serviceModel.OperatorCode,
                        serviceSummary.OperatorName,
                        selectedRoutes.Select(x => x.RouteRef).ToList()));
                }
                else
                {
                    matrixServices.Add(existingService);
                }

                if (!routeMatrixServices.Any() && selectedRoutes.Any() && matrixServices.Count() == 1)
                {
                    var mainRoute = selectedRoutes.OrderByDescending(x => x.JourneyCount).FirstOrDefault();
                    if (mainRoute != null)
                    {
                        routePtpStops.AddRange(mainRoute.StopsAndTimingStatuses.Where(z => z.PrimaryTimingStatus == TimingStatuses.Ptp).ToList());
                    }
                }
            }

            matrix.RouteGroups.ElementAt(routeGroupIndex).UpdateServices(matrixServices);
            if (!routeMatrixServices.Any())
            {
                var stops = routePtpStops.GroupBy(x => x.StopCode).Select(group => new StopAndArrival(group.First().StopCode, false, 0)).ToList();
                matrix.RouteGroups.ElementAt(routeGroupIndex).UpdateStops(stops);
            }

            this.matrixRepository.UpsertMatrix(this.BindMatrixData(matrix));
        }

        public void CompleteMatrices(IEnumerable<string> matrixIds)
        {
            var matrices = this.matrixRepository.GetMatrices(matrixIds);

            foreach (var matrix in matrices)
            {
                matrix.Complete();
            }

            var matrixData = matrices.Select(x => this.BindMatrixData(x));
            foreach (var record in matrixData)
            {
                this.matrixRepository.UpsertMatrix(record);
            }
        }

        public void ActivateMatrices(IEnumerable<string> matrixIds)
        {
            var matrices = this.matrixRepository.GetMatrices(matrixIds);

            foreach (var matrix in matrices)
            {
                matrix.Activate();
            }

            var matrixData = matrices.Select(x => this.BindMatrixData(x));
            foreach (var record in matrixData)
            {
                this.matrixRepository.UpsertMatrix(record);
            }
        }

        public IList<Matrix> SearchCompletedMatrices(string service, DateTime? changeDateFrom, DateTime? changeDateTo)
        {
            return this.matrixRepository.SearchCompletedMatrices(service, changeDateFrom, changeDateTo);
        }

        public IList<Matrix> GetMatricesByServices(IEnumerable<ServiceAndOperatorCode> services)
        {
            return this.matrixRepository.GetMatricesByServices(services);
        }

        private MatrixData BindMatrixData(Matrix matrix)
        {
            var routeGroups = matrix.RouteGroups.Select(x => new MatrixRouteGroupData(
                x.ShowRouteGroupHeader,
                x.RouteGroupHeaderText,
                x.ShowServiceHeaders,
                x.SelectedServices.Select(y => new MatrixRouteGroupServiceData(y.ServiceCode, y.ServiceDisplayText, y.Inbound, y.Outbound, y.OperatorCode, y.OperatorDisplayText, y.SelectedRoutes.ToList())).ToList(),
                x.SelectedStops.Select(y => new MatrixStopData(y.StopCode.Value, y.ShowArrival)),
                x.EnableCompression,
                new MatrixRouteGroupCompressionData(x.Compression.ShowPassedMinutesInCompressionBlock, x.Compression.MaximumFrequencyRange, x.Compression.MaximumCompressionRange, x.Compression.Compression),
                x.TimetableContainerIndex)).ToList();

            MatrixData matrixData = new MatrixData(
                DocumentId.ToObjectId(matrix.Id),
                matrix.Name,
                matrix.CreatedDate,
                matrix.ChangeDate,
                matrix.CompletedDate,
                matrix.IsDeleted,
                matrix.IsCompleted,
                matrix.TemplateId,
                matrix.LayoutId,
                matrix.ServiceTitle,
                matrix.AdditionalInformation,
                matrix.CustomerServiceContactId,
                routeGroups,
                matrix.KeyStopLocations,
                matrix.Logs.Select(e => new StopLogMessageData(e.Timestamp, e.Message, e.Type)),
                matrix.OnlinePdf,
                matrix.AdvancedOptions.Select(a => new AdvancedOptions(a.Rotation, a.ShowNotesAtBottom, a.TimetableContainer)).ToList());

            return matrixData;
        }

        private List<StopLogMessage> GetMatrixGenerationLogs(string matrixId)
        {
            var matrix = this.matrixRepository.GetMatrix(DocumentId.ToObjectId(matrixId));
            var logs = matrix.Logs.Select(x => new StopLogMessage(x.Timestamp, x.Message, x.Type)).ToList();
            return logs;
        }
    }
}