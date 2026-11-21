namespace EncDotNet.S100.Tides.Stations;

/// <summary>A station's predictor on a datum, with where its predictions come from.</summary>
/// <param name="Predictor">The predictor: a <see cref="TidePredictor"/> or a <see cref="SubordinateTidePredictor"/>.</param>
/// <param name="Source">Where the predictions come from.</param>
public sealed record TideStationPrediction(ITidePredictor Predictor, TidePredictionSource Source);
