using System;
using Hexa.NET.ImGui;

namespace OpenEphys.EphysVisualization.Design
{
    /// <summary>
    /// Specifies the button a user chose in a modal drawn by <see cref="ImGuiWidgets.ConfirmModal"/>.
    /// </summary>
    public enum ModalChoice
    {
        /// <summary>
        /// Neither button has been chosen, or the modal is not open.
        /// </summary>
        Pending,

        /// <summary>
        /// The accept button was chosen.
        /// </summary>
        Accepted,

        /// <summary>
        /// The decline button was chosen.
        /// </summary>
        Declined,
    }

    /// <summary>
    /// Provides customized ImGui widgets shared by Open Ephys interfaces.
    /// </summary>
    public static class ImGuiWidgets
    {
        /// <summary>
        /// Draws a label and its value on one line, the label dimmed.
        /// </summary>
        /// <param name="label">The label, drawn followed by a colon.</param>
        /// <param name="value">The value.</param>
        public static void InfoRow(string label, string value)
        {
            ImGui.TextDisabled(label + ":");
            ImGui.SameLine();
            ImGui.TextUnformatted(value);
        }

        /// <summary>
        /// Writes <paramref name="text"/> one character per line, each centered on the region's width, so
        /// that a label fits a column too narrow to hold it across.
        /// </summary>
        /// <param name="text">The label to write.</param>
        public static void StackedLabel(string text)
        {
            var width = ImGui.GetContentRegionAvail().X;
            foreach (var c in text)
            {
                var character = c.ToString();
                var characterWidth = ImGui.CalcTextSize(character).X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, (width - characterWidth) / 2));
                ImGui.TextUnformatted(character);
            }
        }

        // Item ID + timestamp of the most recent click/drag/edit to finish. ImGui's own hover-delay
        // timer accumulates for as long as the mouse sits on an item, click or no click, so a
        // control that was already hovered-with-delay before being clicked stays "primed" through
        // the click and shows its tooltip instantly on release. Tracking this ourselves lets us
        // force the normal delay to re-elapse after an interaction, regardless of ImGui's own timer.
        static uint lastDeactivatedItemId;
        static double lastDeactivatedTime = -1.0;

        /// <summary>
        /// Shows a tooltip for the last item drawn once the pointer has rested on it, including when it is
        /// disabled. Call immediately after the item.
        /// </summary>
        /// <remarks>
        /// The tooltip is not shown while the item is being used, and waits out the hover delay again after
        /// each use rather than appearing the moment the item is released.
        /// </remarks>
        /// <param name="text">The tooltip.</param>
        /// <param name="disabledHint">
        /// A line added to the tooltip while the item is disabled, saying why, or null for none.
        /// </param>
        public static void Tooltip(string text, string? disabledHint = null)
        {
            if (ImGui.IsItemDeactivated())
            {
                lastDeactivatedItemId = ImGui.GetItemID();
                lastDeactivatedTime = ImGui.GetTime();
            }

            if (ImGui.IsItemActive())
                return; // don't show tooltips while the user is interacting (e.g. dragging a slider)

            if (ImGui.GetItemID() == lastDeactivatedItemId &&
                ImGui.GetTime() - lastDeactivatedTime < ImGui.GetStyle().HoverDelayNormal)
                return; // just interacted with this control; wait out the normal delay again

            if (!ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal | ImGuiHoveredFlags.AllowWhenDisabled))
                return; // only show when hovering for a delay and allow for disabled components as well
            bool disabled = ((ImGuiItemFlagsPrivate)ImGuiP.GetItemFlags() & ImGuiItemFlagsPrivate.Disabled) != 0;
            ImGui.SetTooltip(disabled && disabledHint != null ? $"{text}\n{disabledHint}" : text);
        }

        /// <summary>
        /// Draws the modal popup identified by <paramref name="id"/> if it's currently open (via
        /// <see cref="ImGui.OpenPopup(string)"/>), showing <paramref name="message"/> above an accept/decline
        /// button pair. Call every frame regardless of whether the popup is open; it draws nothing otherwise.
        /// </summary>
        /// <param name="id">The popup's ImGui identifier, as passed to <see cref="ImGui.OpenPopup(string)"/>.</param>
        /// <param name="message">The message shown above the buttons.</param>
        /// <param name="acceptLabel">The accept button's label.</param>
        /// <param name="declineLabel">The decline button's label.</param>
        /// <returns>The button chosen this frame, or <see cref="ModalChoice.Pending"/>.</returns>
        public static ModalChoice ConfirmModal(string id, string message, string acceptLabel = "Continue", string declineLabel = "Cancel")
        {
            var choice = ModalChoice.Pending;
            if (ImGui.BeginPopupModal(id, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 25f);
                ImGui.TextUnformatted(message);
                ImGui.PopTextWrapPos();
                ImGui.Spacing();
                if (ImGui.Button(acceptLabel)) { choice = ModalChoice.Accepted; ImGui.CloseCurrentPopup(); }
                ImGui.SameLine();
                if (ImGui.Button(declineLabel)) { choice = ModalChoice.Declined; ImGui.CloseCurrentPopup(); }
                ImGui.EndPopup();
            }
            return choice;
        }
    }
}
