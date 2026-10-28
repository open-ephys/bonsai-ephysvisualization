namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// The channel each row of an owner's data shows, and where each row lies down the plot.
    /// </summary>
    /// <param name="Channels">The channel each row shows, from the top.</param>
    /// <param name="Positions">
    /// How far down the plot each row lies, in any unit and never less than the row above, or null for even
    /// spacing. The closest pair of rows is drawn one row height apart and the rest in proportion, so an empty
    /// stretch shows as a gap of its size, and rows at the same position overlap.
    /// </param>
    public sealed record RowOrder(int[] Channels, float[]? Positions = null);
}
