using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mission.Crosstown.Model;
using Mission.Crosstown.Utility;
using Mission.TimeTable.Domain.Interfaces;
using Mission.TimeTable.Domain.Model.TimeTable;

namespace Mission.TimeTable.Domain.Model.TimeTableGeneration
{
    [DebuggerDisplay("{DepartureTime}")]
    public class TimeTableJourney : IJourneyTime
    {
        public TimeTableJourney(
            TimeTableJourneyPatternSection[] journeyPatternSections,
            Time departureTime,
            string lineName,
            string serviceCode,
            string destination,
            List<Note> notes,
            Stop stop,
            IEnumerable<string> regularDayOperation = null,
            bool hasServicedOrganizations = false,
            bool isArrivalTime = false)
        {
            this.JourneyPatternSections = journeyPatternSections;
            this.DepartureTime = departureTime;
            this.LineName = lineName;
            this.ServiceCode = serviceCode;
            this.Destination = destination;
            this.Notes = notes;
            this.RegularDayOperation = regularDayOperation;
            this.HasServicedOrganizations = hasServicedOrganizations;
            this.Stop = stop;
            this.IsArrivalTime = isArrivalTime;
        }

        public TimeTableJourneyPatternSection[] JourneyPatternSections { get; private set; }

        public Time DepartureTime { get; private set; }

        public string TriangleDepartureTime { get; set; }

        public List<Note> Notes { get; private set; }

        public string LineName { get; private set; }

        public string ServiceCode { get; private set; }

        public string Destination { get; private set; }

        public bool IsArrivalTime { get; set; }

        public int StopIndex { get; set; }

        public Stop Stop { get; private set; }

        public IEnumerable<string> RegularDayOperation { get; private set; }

        public bool HasServicedOrganizations { get; private set; }

        public string DisplayText => this.DepartureTime.ToString();

        public bool HasNotes => this.Notes.Any();

        public bool IsTriangle { get; private set; }

        public string NoteBackgroundColor { get; private set; }

        public bool IsEmpty { get; private set; }

        public void SetJourneyInfo(bool isTriangle, string noteBackgroundColor, bool isEmpty, int stopIndex)
        {
            this.IsTriangle = isTriangle;
            this.NoteBackgroundColor = noteBackgroundColor;
            this.IsEmpty = isEmpty;
            this.StopIndex = stopIndex;
        }

        public bool HasDepartures(AtcoCode atcoCode)
        {
            return this.JourneyPatternSections.SelectMany(e => e.TimingLinks).WithoutLast().Any(x => x.FromAtcoCode == atcoCode);
        }

        public bool HasDeparturesWithLastStop(AtcoCode atcoCode)
        {
            return this.JourneyPatternSections.SelectMany(e => e.TimingLinks).Any(x => x.FromAtcoCode == atcoCode);
        }
    }
}
