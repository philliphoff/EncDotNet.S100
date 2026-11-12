namespace EncDotNet.S100.Tides.Stations;

/// <summary>A current station's predictor, with where its predictions come from.</summary>
/// <param name="Predictor">The predictor: a <see cref="CurrentPredictor"/> or a <see cref="SubordinateCurrentPredictor"/>.</param>
/// <param name="Source">Where the predictions come from.</param>
public sealed record CurrentStationPrediction(ICurrentPredictor Predictor, CurrentPredictionSource Source);
