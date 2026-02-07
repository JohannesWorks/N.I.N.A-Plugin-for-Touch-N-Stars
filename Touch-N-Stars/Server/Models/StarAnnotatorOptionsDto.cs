namespace TouchNStars.Server.Models {

    /// <summary>
    /// DTO for serializing/deserializing StarAnnotatorOptions
    /// </summary>
    public class StarAnnotatorOptionsDto {
        public bool ShowAnnotations { get; set; }
        public bool ShowAllStars { get; set; }
        public int MaxStars { get; set; }
        public bool ShowStarBounds { get; set; }
        public string StarBoundsType { get; set; }
        public string StarBoundsColor { get; set; }
        public string ShowAnnotationType { get; set; }
        public string AnnotationColor { get; set; }
        public string AnnotationFontFamily { get; set; }
        public float AnnotationFontSizePoints { get; set; }
        public bool ShowROI { get; set; }
        public string ROIColor { get; set; }
        public bool ShowStarCenter { get; set; }
        public string StarCenterColor { get; set; }
        public string ShowStructureMap { get; set; }
        public string StructureMapColor { get; set; }
        public string TooFlatColor { get; set; }
        public string SaturatedColor { get; set; }
        public string LowSensitivityColor { get; set; }
        public string NotCenteredColor { get; set; }
        public string DegenerateColor { get; set; }
        public string TooDistortedColor { get; set; }
        public bool ShowTooDistorted { get; set; }
        public bool ShowDegenerate { get; set; }
        public bool ShowSaturated { get; set; }
        public bool ShowLowSensitivity { get; set; }
        public bool ShowNotCentered { get; set; }
        public bool ShowTooFlat { get; set; }
    }
}
