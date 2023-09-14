using Mission.TimeTable.Domain.GPE.Actions.RenderTimetable;
using Mission.TimeTable.Domain.GPE.GPEAttribute;
using Mission.TimeTable.Domain.GPE.LogMessageGenerator;
using Mission.TimeTable.Domain.Utility;
using Mission.TimeTable.Logger.Interfaces;
using Svg;
using Svg.Transforms;

namespace Mission.TimeTable.Domain.GPE.Actions
{
    [GpeAction(Name = "Replace Text", MasterElement = "SvgText")]
    public class ReplaceTextAction : GenericActions
    {
        public ReplaceTextAction(string elementId, string newText)
        {
            this.ElementId = elementId;
            this.NewText = newText;
        }

        private string ElementId { get; }

        private string NewText { get; }

        public override void Execute(SvgDocument document, IBatchLogger batchLogger, bool isRoadSide)
        {
            if (string.IsNullOrEmpty(this.NewText) && this.ElementId != "AdditionalInformation")
            {
                batchLogger.LogWarning(MessageGenerator.WarningMessage($"{this.ElementId} is Empty. ", MessageGenerator.GetSourceName(this.GetType())), false);
            }

            this.Validate(document, this.ElementId);
            var textElement = document.GetElementById<SvgText>(this.ElementId);

            textElement.Content = this.NewText ?? string.Empty;
            textElement.Text = this.NewText ?? string.Empty;

            textElement.Nodes.Clear();
            textElement.Nodes.Add(new SvgContentNode { Content = this.NewText ?? string.Empty });

            if (this.ElementId == TemplateConventionsConfig.AdditionalInformation && !isRoadSide)
            {
                this.SetMatrixAdditionalInformationTextPosition(document, textElement);
            }
            else
            {
                var elementTransform = textElement.Transforms.GetMatrix().OffsetX + textElement.Bounds.Width;
                var templateBounds = document.Width - 50;
                var gapBetweenElements = elementTransform - templateBounds;
                if (elementTransform > templateBounds)
                {
                    textElement.Transforms.Add(new SvgTranslate(-(gapBetweenElements / 2)));
                }
            }
        }

        private void SetMatrixAdditionalInformationTextPosition(SvgDocument document, SvgText textElement)
        {
            var timetableContainer = document.GetElementById<SvgRectangle>(TimetablePropertiesConfig.TimetableContainer);

            if (timetableContainer == null)
            {
                return;
            }

            float additionalInformationXPosition = (timetableContainer.Bounds.Width + timetableContainer.X) - textElement.Bounds.Width;
            textElement.Transforms.Add(new SvgTranslate(additionalInformationXPosition, textElement.Transforms.GetMatrix().OffsetY));
            textElement.Transforms.RemoveAt(0);
        }
    }
}
