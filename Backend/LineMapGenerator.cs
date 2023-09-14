using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Microsoft.Practices.ObjectBuilder2;
using Mission.TimeTable.Domain.GPE.Actions.LineMap;
using Mission.TimeTable.Domain.GPE.Actions.LineMap.Factories;
using Mission.TimeTable.Domain.GPE.Actions.LineMap.NodeJoins;
using Mission.TimeTable.Domain.GPE.Actions.LineMap.Nodes;
using Mission.TimeTable.Domain.GPE.Actions.RenderTimetable;
using Mission.TimeTable.Domain.GPE.Models;
using Mission.TimeTable.Domain.GPE.Utilities;
using Mission.TimeTable.Domain.Model.TimeTable.SectionStyles;
using Svg;
using Svg.Transforms;

namespace Mission.TimeTable.Domain.GPE.Actions
{
    public class LineMapGenerator
    {
        public const float HorizontalMargin = 4f * TimetablePropertiesConfig.PixelsPerMilliMeters;

        private readonly LineMapSectionStyle sectionStyle;

        public LineMapGenerator(ServiceLineMapsViewModel serviceLinemap, LineMapSectionStyle sectionStyle)
        {
            this.ServiceLinemap = serviceLinemap;
            this.sectionStyle = sectionStyle;
        }

        public double Width { get; set; }

        public double Height { get; set; }

        public float NodeJoinsCount => this.ServiceLinemap.LineStops.Count - 1;

        public float MinimumLinemapLengthInMM => this.CalculateLinemapWidthInMM(TimetablePropertiesConfig.MinimumJoinLength);

        public float MaximumLinemapLengthInMM => this.CalculateLinemapWidthInMM(TimetablePropertiesConfig.JoinLength);

        public ServiceLineMapsViewModel ServiceLinemap { get; private set; }

        public void Generate(SvgElement parent, SvgRectangle boundingElement, double left, double top)
        {
            SvgGroup linemap = new SvgGroup();
            linemap.ID = this.ServiceLinemap.ServiceCode;
            parent.Children.Add(linemap);

            var lineMapInfo = this.GetLineMap(this.ServiceLinemap.LineStops, 0);
            var stopNodes = lineMapInfo.Where(x => x is CircleNode);
            var routes = stopNodes.Select(x => x.StopsDetail.RouteRef).Distinct();
            var firstRouteStopCount = stopNodes.Count(x => x.StopsDetail.RouteRef == routes.First());
            var totalStopCount = firstRouteStopCount;

            if (lineMapInfo.Any(x => x is CurvedNodeJoin))
            {
                foreach (var route in routes.Skip(1))
                {
                    var curveJoinConnectingToRoute = lineMapInfo.SingleOrDefault(x => x is CurvedNodeJoin && x.RouteConnectingTo == route);
                    var indexOfJoin = lineMapInfo.ToList().IndexOf(curveJoinConnectingToRoute);

                    var numberOfStopsInRoute = stopNodes.Count(x => x.StopsDetail.RouteRef == route);
                    var numberOfPreviousStops = lineMapInfo.Take(indexOfJoin).Count(x => x is CircleNode);

                    var numberOfStopsLongerThanFirstRoute = (numberOfPreviousStops + numberOfStopsInRoute) - firstRouteStopCount;

                    if (numberOfStopsLongerThanFirstRoute > 0 && (numberOfPreviousStops + numberOfStopsInRoute) > totalStopCount)
                    {
                        totalStopCount += numberOfStopsLongerThanFirstRoute;
                    }
                }
            }

            var joinLength = this.CalculateRequiredJoinLength(lineMapInfo, boundingElement.Width - (2 * HorizontalMargin), totalStopCount);
            joinLength = Math.Max(joinLength, TimetablePropertiesConfig.MinimumJoinLength);
            joinLength = Math.Min(joinLength, TimetablePropertiesConfig.JoinLength);

            lineMapInfo = this.GetLineMap(this.ServiceLinemap.LineStops, joinLength);

            this.Width = this.RenderLineMap(parent, lineMapInfo, left, top);

            this.Height += lineMapInfo.Where(x => x.StopsDetail != null).Max(x => x.Height) - TimetablePropertiesConfig.NodeSize;

            parent.Transforms.Add(new SvgTranslate((float)left - boundingElement.X + HorizontalMargin, (float)this.Height));
        }

        private double RenderLineMap(SvgElement parent, LineMapSegment[] lineMap, double left, double top)
        {
            var lineMapGroups = this.CreateLineMapGroups(lineMap);
            lineMapGroups.ForEach(x => parent.Children.Add(x.Value));

            string currentRoute = lineMap.First().StopsDetail.RouteRef;
            List<double> linemapHeights = new List<double>();

            int segmentIndex = 0;
            int curveJoinIndex = 0;
            int routeIndex = 0;
            List<LineMapRoutePosition> lineMapRoutePositions = new List<LineMapRoutePosition>();

            var isMultiRouteLineMap = lineMap.Any(x => x.StopsDetail?.NextStops.Count() > 1);

            foreach (var segment in lineMap)
            {
                if (segment.StopsDetail != null && currentRoute != segment.StopsDetail.RouteRef && isMultiRouteLineMap)
                {
                    var nextRouteX = lineMapRoutePositions.SingleOrDefault(x => x.RouteRef == segment.StopsDetail.RouteRef)?.X;

                    if (lineMap.ElementAt(segmentIndex - 1) is CircleNode || lineMap.ElementAt(segmentIndex - 1) is CustomNode)
                    {
                        top += this.CalculateHeightOfNextRoutes(lineMap, new[] { segment.StopsDetail.RouteRef });
                    }
                    
                    linemapHeights.Add(left);
                    left = nextRouteX ?? left;
                    currentRoute = segment.StopsDetail.RouteRef;
                    routeIndex++;
                }

                var nextSegmentX = segment.NextConnectors.Single().X;

                if (segment is CurvedNodeJoin curvedJoin)
                {
                    var previousSegment = lineMap.ElementAt(segmentIndex - 1);

                    if (previousSegment is CurvedNodeJoin)
                    {
                        nextSegmentX = 0;

                        lineMapRoutePositions.Add(new LineMapRoutePosition(segment.RouteConnectingTo, left + segment.Width - previousSegment.Width));
                    }
                    else
                    {
                        lineMapRoutePositions.Add(new LineMapRoutePosition(segment.RouteConnectingTo, left + segment.Width));
                    }

                    var connectingRoutes = this.GetConnectingRoutesFromRoute(lineMap, currentRoute);

                    var currentRouteIndex = connectingRoutes.ToList().IndexOf(segment.RouteConnectingTo);
                    var nextRoutes = connectingRoutes.Skip(currentRouteIndex).ToList();
                    curvedJoin.SetHeight(this.CalculateHeightOfNextRoutes(lineMap, nextRoutes));
                    curveJoinIndex++;
                }

                segment.Render(lineMapGroups[segment.ZOrder], left, top);
                left += nextSegmentX;

                segmentIndex++;
            }

            linemapHeights.Add(left);
            return linemapHeights.Max();
        }

        private IEnumerable<string> GetConnectingRoutesFromRoute(LineMapSegment[] lineMap, string currentRoute)
        {
            var currentRouteSegments = this.GetCurrentRouteLineMapSegments(lineMap, currentRoute);
            var currentRouteConnectingRoutes = currentRouteSegments.Where(x => x is CurvedNodeJoin).Select(x => x.RouteConnectingTo).ToList();

            List<string> allConnectingRoutesFromCurrentRoute = new List<string>();
            foreach (var route in currentRouteConnectingRoutes)
            {
                var routeSegments = this.GetCurrentRouteLineMapSegments(lineMap, route);
                allConnectingRoutesFromCurrentRoute.AddRange(routeSegments.Where(x => x is CurvedNodeJoin).Select(x => x.RouteConnectingTo));
                allConnectingRoutesFromCurrentRoute.Add(route);
            }

            return allConnectingRoutesFromCurrentRoute;
        }

        private IEnumerable<LineMapSegment> GetCurrentRouteLineMapSegments(LineMapSegment[] lineMap, string currentRoute)
        {
            var lineMapSegments = lineMap.Where(x => x.StopsDetail != null);
            var firstSegmentInRoute = lineMapSegments.First(x => x.StopsDetail.RouteRef == currentRoute);
            var lastSegmentInRoute = lineMapSegments.Last(x => x.StopsDetail.RouteRef == currentRoute);

            int firstSegmentInRouteIndex = lineMap.ToList().IndexOf(firstSegmentInRoute);
            int lastSegmentInRouteIndex = lineMap.ToList().IndexOf(lastSegmentInRoute);
            return lineMap.Skip(firstSegmentInRouteIndex).Take(lastSegmentInRouteIndex - firstSegmentInRouteIndex);
        }

        private float CalculateHeightOfNextRoutes(IEnumerable<LineMapSegment> lineMap, IEnumerable<string> nextRouteRefs)
        {
            float height = 0;

            foreach (var routeRef in nextRouteRefs)
            {
                var firstIndex = lineMap.ToList().FindIndex(x => x.StopsDetail?.RouteRef == routeRef);
                var lastIndex = lineMap.ToList().FindLastIndex(x => x.StopsDetail?.RouteRef == routeRef);

                var segments = lineMap.Skip(firstIndex).Take(lastIndex - firstIndex + 1);

                SvgDocument document = new SvgDocument();

                foreach (var segment in segments)
                {
                    segment.Render(document, 0, 0);
                }

                height += segments.Max(x => x.Height) + TimetablePropertiesConfig.PaddingBetweenElements;
            }

            return height;
        }

        private float CalculateRequiredJoinLength(LineMapSegment[] lineMap, float desiredWidth, int numberOfUniqueStops)
        {
            float sizeOfFixedElements = 0f;

            SvgDocument document = new SvgDocument();

            foreach (var segment in lineMap.Where(x => !x.CanBeResized).GroupBy(e => e.StopsDetail.StopCode))
            {
                segment.First().Render(document, 0, 0);
                sizeOfFixedElements += segment.First().Width;
            }

            sizeOfFixedElements += lineMap.Last().SegmentWidth - lineMap.Last().Width;

            return (desiredWidth - sizeOfFixedElements) / numberOfUniqueStops;
        }

        private Dictionary<int, SvgGroup> CreateLineMapGroups(LineMapSegment[] lineMap)
        {
            return lineMap
                .Select(x => x.ZOrder)
                .Distinct()
                .OrderBy(x => x)
                .ToDictionary(x => x, x => new SvgGroup());
        }

        private LineMapSegment[] GetLineMap(List<LineMapStopDetail> lineStops, float joinLength)
        {
            var segments = new List<LineMapSegment>();

            var factory = new LineMapFactory(TimetablePropertiesConfig.NodeSize, joinLength, 5, this.sectionStyle);

            List<string> routesAlreadyConnectedTo = new List<string>();

            int routeIndex = 0;

            if (lineStops.Count > 1)
            {
                var routes = lineStops.GroupBy(x => x.RouteRef);

                foreach (var route in routes)
                {
                    bool addRestOfRoute = false;
                    int currentStopInFirstRouteIndex = routes.First().Select(x => x.StopCode).ToList().IndexOf(route.First().StopCode);
                    int stopIndex = 0;

                    foreach (var stop in route)
                    {
                        var stopExistsInPreviousRoute = routes.First().ElementAtOrDefault(currentStopInFirstRouteIndex)?.StopCode == stop.StopCode;

                        var curvedJoinConnectingToCurrentRoute = segments.SingleOrDefault(x => x.RouteConnectingTo == route.Key);
                        if (curvedJoinConnectingToCurrentRoute != null)
                        {
                            var indexOfCurveJoin = segments.IndexOf(curvedJoinConnectingToCurrentRoute);
                            var previousConnectedRouteRef = segments.ElementAt(indexOfCurveJoin - 1).StopsDetail?.RouteRef ?? segments.ElementAt(indexOfCurveJoin - 2).StopsDetail.RouteRef;                         

                            stopExistsInPreviousRoute = routes.SingleOrDefault(x => x.Key == previousConnectedRouteRef).ElementAtOrDefault(currentStopInFirstRouteIndex)?.StopCode == stop.StopCode;
                        }

                        if (!segments.Any() || !stopExistsInPreviousRoute || addRestOfRoute)
                        {
                            if (segments.Any()
                                && routes.ElementAtOrDefault(routeIndex - 1)?.Last().StopCode == route.ElementAtOrDefault(stopIndex - 1)?.StopCode
                                && !segments.Where(x => x is CircleNode).Any(x => x.StopsDetail.RouteRef == route.Key)
                                && route.Key != routes.First().Key)
                            {
                                segments.Add(factory.MakeOneToOneNodeJoin());
                            }

                            if (stop.ShowIAmHere)
                            {
                                segments.Add(factory.YouAreHereNode(stop));
                            }
                            else if (!string.IsNullOrEmpty(stop.CustomNodeFile.Href))
                            {
                                segments.Add(factory.CustomNode(stop));
                            }
                            else
                            {
                                segments.Add(factory.MakeNode(stop));
                            }

                            var indexOfNextStop = route.ToList().IndexOf(stop);
                            var nextStop = route.ElementAtOrDefault(indexOfNextStop + 1);
                            if (nextStop != null)
                            {
                                var nextStopInCurrentRoute = stop.NextStops.SingleOrDefault(y => y.RouteRef == route.Key)?.StopCode;
                                string nextRouteRef = stop.NextStops.Where(x => x.RouteRef != route.Key && x.StopCode != nextStopInCurrentRoute && !routesAlreadyConnectedTo.Contains(x.RouteRef)).FirstOrDefault()?.RouteRef;
                                
                                if (stop.NextStops?.Count() > 1 && route.Key != routes.Last().Key && nextRouteRef != null && nextRouteRef != routes.First().Key)
                                {
                                    segments.Add(factory.MakeCurvedNodeJoin(true, nextRouteRef));

                                    routesAlreadyConnectedTo.Add(nextRouteRef);

                                    var numberOfRoutesToJoin = stop.NextStops.Select(x => x.StopCode).Distinct().Count();

                                    for (int i = 2; i < numberOfRoutesToJoin; i++)
                                    {
                                        nextRouteRef = stop.NextStops.Where(x => x.RouteRef != route.Key && x.StopCode != nextStopInCurrentRoute && !routesAlreadyConnectedTo.Contains(x.RouteRef)).First().RouteRef;
                                        segments.Add(factory.MakeCurvedNodeJoin(false, nextRouteRef));
                                        routesAlreadyConnectedTo.Add(nextRouteRef);
                                    }
                                }
                                else
                                {
                                    segments.Add(factory.MakeOneToOneNodeJoin());
                                }
                            }

                            addRestOfRoute = true;
                        }

                        currentStopInFirstRouteIndex++;
                        stopIndex++;
                    }

                    routeIndex++;
                }
            }

            return segments.ToArray();
        }

        private float GetLastStopPointTextWidth(LineMapStopDetail lastStop, SvgFontWeight fontweight)
        {
            var stopNameWidth = this.GetTextWidth(lastStop.StopName, fontweight);
            var roadNameWidth = this.GetTextWidth(lastStop.RoadName, fontweight);

            return stopNameWidth > roadNameWidth ? stopNameWidth : roadNameWidth;
        }

        private float GetTextWidth(string message, SvgFontWeight fontweight)
        {
            SvgDocument textDocument = new SvgDocument();
            textDocument.Width = 500;
            textDocument.Height = 500;
            var textElement = this.GetNewText(message, fontweight);
            textDocument.Children.Add(textElement);

            return NodeHelper.CalculateTextWidth(textElement);
        }

        private SvgText GetNewText(string content, SvgFontWeight fontweight)
        {
            var newText = new SvgText();
            newText.FontSize = new SvgUnit(SvgUnitType.Pixel, this.sectionStyle.FontInfo.FontSize);
            newText.FontFamily = this.sectionStyle.FontInfo.FontFamily;
            newText.FontWeight = fontweight;
            newText.Nodes.Add(new SvgContentNode { Content = content });
            newText.Fill = new SvgColourServer(this.sectionStyle.TextColor);
            newText.ApplyFontStyle(this.sectionStyle.FontInfo.FontStyle);
            return newText;
        }

        private float CalculateLinemapWidthInMM(float joinLength)
        {
            var singleElementWidth = TimetablePropertiesConfig.NodeSize + joinLength;
            var totalElementsLength = (singleElementWidth * this.NodeJoinsCount) + TimetablePropertiesConfig.NodeSize;
            var lastStopTextWidth = this.GetLastStopPointTextWidth(this.ServiceLinemap.LineStops.Last(), SvgFontWeight.W600);

            return (totalElementsLength + lastStopTextWidth) / TimetablePropertiesConfig.PixelsPerMilliMeters;
        }
    }
}