namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// The channel each row of an owner's data shows, where each row lies down the plot, and which lane it is in.
    /// </summary>
    /// <param name="Channels">The channel each row shows, from the top.</param>
    /// <param name="Positions">
    /// How far down the plot each row lies, in any unit and never less than the row above in its lane, or null for
    /// even spacing. The closest pair of rows in a lane is drawn one row height apart and the rest in proportion, so
    /// an empty stretch shows as a gap of its size, and rows at the same position overlap.
    /// </param>
    /// <param name="Lanes">
    /// The lane each row is drawn in, or null for one lane. Lanes sit side by side in the order their rows come, and
    /// a lane's rows must come together. Positions line up across lanes, so equal positions are level.
    /// </param>
    public sealed record RowOrder(int[] Channels, float[]? Positions = null, int[]? Lanes = null);
}
