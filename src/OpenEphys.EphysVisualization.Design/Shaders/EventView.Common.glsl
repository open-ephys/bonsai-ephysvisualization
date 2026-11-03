// Amplitude bins are spaced evenly in asinh(value / knee), so they are fine near zero and coarser at large
// amplitudes, a near constant fraction of the amplitude anywhere. The same mapping as ImGuiEventView.BinOf,
// kept to the tile's rows, so a value beyond the span lands on its edge rather than in the next tile.
uniform float knee;
uniform float span;
uniform int bins;

float binOf(float value)
{
    float v = clamp(value, -span, span);
    float bin = (asinh(v / knee) / asinh(span / knee) * 0.5 + 0.5) * float(bins);
    return clamp(bin, 0.5, float(bins) - 0.5);
}
