namespace TouchNStars.Server.Models {

    /// <summary>
    /// DTO for serializing/deserializing StarDetectionOptions
    /// </summary>
    public class StarDetectionOptionsDto {
        public bool UseAdvanced { get; set; }
        public bool ModelPSF { get; set; }
        public string Simple_NoiseLevel { get; set; }
        public string Simple_PixelScale { get; set; }
        public string Simple_FocusRange { get; set; }
        public bool HotpixelFiltering { get; set; }
        public bool HotpixelThresholdingEnabled { get; set; }
        public bool UseAutoFocusCrop { get; set; }
        public int NoiseReductionRadius { get; set; }
        public double NoiseClippingMultiplier { get; set; }
        public double StarClippingMultiplier { get; set; }
        public int StructureLayers { get; set; }
        public double BrightnessSensitivity { get; set; }
        public double StarPeakResponse { get; set; }
        public double MaxDistortion { get; set; }
        public double StarCenterTolerance { get; set; }
        public int StarBackgroundBoxExpansion { get; set; }
        public int MinStarBoundingBoxSize { get; set; }
        public double MinHFR { get; set; }
        public int StructureDilationSize { get; set; }
        public int StructureDilationCount { get; set; }
        public double PixelSampleSize { get; set; }
        public bool DebugMode { get; set; }
        public string IntermediateSavePath { get; set; }
        public bool SaveIntermediateImages { get; set; }
        public int PSFParallelPartitionSize { get; set; }
        public bool StarMeasurementNoiseReductionEnabled { get; set; }
        public string PSFFitType { get; set; }
        public int PSFResolution { get; set; }
        public double PSFFitThreshold { get; set; }
        public bool UsePSFAbsoluteDeviation { get; set; }
        public double HotpixelThreshold { get; set; }
        public double SaturationThreshold { get; set; }
        public string MeasurementAverage { get; set; }
    }
}
