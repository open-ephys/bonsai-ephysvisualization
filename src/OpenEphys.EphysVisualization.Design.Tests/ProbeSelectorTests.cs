using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using Newtonsoft.Json;
using OpenEphys.ProbeInterface.NET;
using Xunit;

namespace OpenEphys.EphysVisualization.Design.Tests
{
    [Collection(ImGuiCollection.Name)]
    public sealed class ProbeSelectorTests : IDisposable
    {
        const int Columns = 2;
        const int Rows = 10;
        const int Count = Columns * Rows;

        readonly ImGuiHarness imgui = new();
        readonly ImGuiProbeSelector selector = new(zoomWindowFraction: 1);
        readonly Rect[] contacts = new Rect[Count];
        bool selectionEnabled = true;
        bool rulerControls;
        Vector2 addButton, clearButton;
        int selectionChanges;
        readonly Action draw;

        public ProbeSelectorTests()
        {
            draw = () =>
            {
                if (rulerControls)
                {
                    addButton = ImGui.GetCursorScreenPos() + new Vector2(4);
                    selector.DrawRulerControls();
                    clearButton = ImGui.GetItemRectMin() + new Vector2(4);
                }
                selector.DrawZoomedView(selectionEnabled);
            };
            selector.Refresh(Probe(Columns, Rows));
            selector.SelectionChanged += (_, _) => selectionChanges++;

            // NB: each contact filled with a color of its own, so that where it is drawn can be found in the draw data.
            selector.FillColor = Fill;
            imgui.Frames(2, draw);
            for (int i = 0; i < Count; i++)
                contacts[i] = Assert.Single(ImGuiHarness.Rects(Fill(i)));
        }

        public void Dispose() => imgui.Dispose();

        static uint Fill(int contact) => 0xFF000000 | (uint)(contact + 1);

        [Fact]
        public void Refresh_ListsEveryContact_NoneSelected()
        {
            Assert.Equal(Count, selector.Contacts.Count);
            Assert.All(selector.Contacts, c => Assert.Equal(default, c));
        }

        [Fact]
        public void TheContacts_AreDrawnWhereTheProbePutsThem()
        {
            // NB: y up on the probe and down on screen.
            Assert.True(contacts[1].Center.X > contacts[0].Center.X);
            Assert.True(contacts[2].Center.Y < contacts[0].Center.Y);
            var first = contacts[0].Max - contacts[0].Min;
            var last = contacts[Count - 1].Max - contacts[Count - 1].Min;
            Assert.True(Vector2.Distance(first, last) < 1e-3f, $"The first contact is {first} and the last {last}.");
        }

        [Fact]
        public void ClickingAContact_SelectsItAlone()
        {
            Click(5);
            Assert.Equal(new[] { 5 }, Selected());
            Assert.Equal(new ContactState(true, true, true), selector.Contacts[5]);

            Click(7);
            Assert.Equal(new[] { 7 }, Selected());
            Assert.Equal(2, selectionChanges);
        }

        [Fact]
        public void ShiftClick_AddsToTheSelection()
        {
            Click(5);
            imgui.Hold(ImGuiKey.LeftShift, true, draw);
            Click(7);
            imgui.Hold(ImGuiKey.LeftShift, false, draw);

            Assert.Equal(new[] { 5, 7 }, Selected());
        }

        [Fact]
        public void ClickingBesideTheContacts_ClearsTheSelection()
        {
            Click(5);
            var beside = new Vector2(contacts[1].Max.X + 20, contacts[1].Center.Y);
            ClickAt(beside);

            Assert.Empty(Selected());
        }

        [Fact]
        public void ADrag_SelectsTheContactsItSweeps()
        {
            Drag(contacts[4].Center, contacts[9].Center);

            Assert.Equal(new[] { 4, 5, 6, 7, 8, 9 }, Selected());
            Assert.All(new[] { 4, 5, 6, 7, 8, 9 }, i => Assert.True(selector.Contacts[i].InDragBox));
            Assert.Equal(1, selectionChanges);
        }

        [Fact]
        public void ADrag_ShowsWhatItDoesWhileHeld()
        {
            imgui.MoveMouse(contacts[4].Center, draw);
            imgui.Button(ImGuiMouseButton.Left, true, draw);
            imgui.MoveMouse(contacts[9].Center, draw);

            Assert.Equal(DragSelectIntent.Add, selector.DragIntent);
            Assert.Equal(new[] { 4, 5, 6, 7, 8, 9 }, Selected());
            Assert.Equal(0, selectionChanges);

            imgui.Button(ImGuiMouseButton.Left, false, draw);
            Assert.Equal(1, selectionChanges);
        }

        [Fact]
        public void CtrlShiftDrag_RemovesTheContactsItSweeps()
        {
            Drag(contacts[0].Center, contacts[Count - 1].Center);

            imgui.Hold(ImGuiKey.LeftShift, true, draw);
            imgui.Hold(ImGuiKey.LeftCtrl, true, draw);
            imgui.MoveMouse(contacts[4].Center, draw);
            imgui.Button(ImGuiMouseButton.Left, true, draw);
            imgui.MoveMouse(contacts[9].Center, draw);
            Assert.Equal(DragSelectIntent.Remove, selector.DragIntent);
            imgui.Button(ImGuiMouseButton.Left, false, draw);
            imgui.Hold(ImGuiKey.LeftCtrl, false, draw);
            imgui.Hold(ImGuiKey.LeftShift, false, draw);

            Assert.Equal(Enumerable.Range(0, Count).Except(Enumerable.Range(4, 6)), Selected());
        }

        [Fact]
        public void BlockedContacts_AreInspectedButNotSelected()
        {
            selector.Blocked = i => i % 2 == 0;

            Drag(contacts[4].Center, contacts[9].Center);

            Assert.Equal(new[] { 5, 7, 9 }, Selected());
            Assert.Equal(new[] { 4, 5, 6, 7, 8, 9 }, Indices(c => c.Inspected));
        }

        [Fact]
        public void BlockedContacts_AreSelected_WhenSelectionDoesNotSkipThem()
        {
            selector.Blocked = i => i % 2 == 0;
            selector.SelectionSkipsBlocked = false;

            Drag(contacts[4].Center, contacts[9].Center);

            Assert.Equal(new[] { 4, 5, 6, 7, 8, 9 }, Selected());
        }

        [Fact]
        public void Escape_ClearsTheSelection()
        {
            Drag(contacts[4].Center, contacts[9].Center);

            imgui.Key(ImGuiKey.Escape, draw);

            Assert.All(selector.Contacts, c => Assert.Equal(default, c));
            Assert.Equal(2, selectionChanges);
        }

        [Fact]
        public void NothingIsSelected_WhileSelectionIsDisabled()
        {
            selectionEnabled = false;

            Click(5);
            Drag(contacts[4].Center, contacts[9].Center);

            Assert.Empty(Selected());
            Assert.Equal(0, selectionChanges);
        }

        [Fact]
        public void R_ArmsOneRuler_WhichADragPlaces()
        {
            PlaceRuler(0, 18);

            Assert.Equal(1, Rulers());
            Assert.Empty(Selected());

            Click(5);
            Assert.Equal(new[] { 5 }, Selected());
            Assert.Equal(1, Rulers());
        }

        [Fact]
        public void AnArmedRuler_CanBePlacedWithTwoClicks()
        {
            imgui.MoveMouse(contacts[0].Center, draw);
            imgui.Key(ImGuiKey.R, draw);

            Click(0);
            Click(18);

            Assert.Equal(1, Rulers());
            Assert.Empty(Selected());
        }

        [Fact]
        public void R_Again_DisarmsTheRuler()
        {
            imgui.MoveMouse(contacts[0].Center, draw);
            imgui.Key(ImGuiKey.R, draw);
            imgui.Key(ImGuiKey.R, draw);

            Click(5);

            Assert.Equal(new[] { 5 }, Selected());
            Assert.Equal(0, Rulers());
        }

        [Fact]
        public void AnArmedRuler_HidesThePointer_ForACrosshair()
        {
            imgui.MoveMouse(contacts[0].Center, draw);
            Assert.Equal(ImGuiMouseCursor.Arrow, ImGui.GetMouseCursor());

            imgui.Key(ImGuiKey.R, draw);
            Assert.Equal(ImGuiMouseCursor.None, ImGui.GetMouseCursor());

            imgui.Key(ImGuiKey.Escape, draw);
            Assert.Equal(ImGuiMouseCursor.Arrow, ImGui.GetMouseCursor());
        }

        [Fact]
        public void TheAddButton_ArmsARuler_AndPressingItAgainCancelsIt()
        {
            ShowRulerControls();

            ClickAt(addButton);
            Assert.True(Placing());
            Drag(contacts[0].Center, contacts[18].Center);
            Assert.Equal(1, Rulers());

            ClickAt(addButton);
            Click(0);
            ClickAt(addButton);
            Assert.False(Placing());
            Click(18);
            Assert.Equal(1, Rulers());
        }

        [Fact]
        public void TheClearButton_RemovesEveryRuler()
        {
            ShowRulerControls();
            PlaceRuler(0, 18);
            PlaceRuler(1, 19);

            ClickAt(clearButton);

            Assert.Equal(0, Rulers());
        }

        [Fact]
        public void APlacedRuler_IsSelected()
        {
            PlaceRuler(0, 18);
            PlaceRuler(1, 19);

            Assert.Equal(1, SelectedRuler());
        }

        [Fact]
        public void ClickingARuler_SelectsIt_WithoutTouchingTheContacts()
        {
            Click(5);
            PlaceRuler(0, 18);
            PlaceRuler(1, 19);

            // NB: contact 8 lies under the first ruler.
            Click(8);

            Assert.Equal(0, SelectedRuler());
            Assert.Equal(new[] { 5 }, Selected());
        }

        [Fact]
        public void ClickingAwayFromTheRulers_DeselectsThem()
        {
            PlaceRuler(0, 18);

            Click(5);

            Assert.Equal(-1, SelectedRuler());
            Assert.Equal(new[] { 5 }, Selected());
        }

        [Fact]
        public void Delete_RemovesTheSelectedRuler()
        {
            PlaceRuler(0, 18);
            PlaceRuler(1, 19);
            Click(8);

            imgui.Key(ImGuiKey.Delete, draw);

            Assert.Equal(1, Rulers());
            Assert.Equal(-1, SelectedRuler());
        }

        [Fact]
        public void ShiftDelete_RemovesEveryRuler()
        {
            PlaceRuler(0, 18);
            PlaceRuler(1, 19);

            imgui.Key(ImGuiKey.LeftShift, ImGuiKey.Delete, draw);

            Assert.Equal(0, Rulers());
        }

        [Fact]
        public void Escape_CancelsARuler_ThenDeselectsOne_ThenClearsTheSelection()
        {
            Click(5);
            PlaceRuler(0, 18);
            imgui.Key(ImGuiKey.R, draw);
            Click(1);

            imgui.Key(ImGuiKey.Escape, draw);
            Assert.Equal(1, Rulers());
            Assert.Equal(0, SelectedRuler());
            Assert.Equal(new[] { 5 }, Selected());

            imgui.Key(ImGuiKey.Escape, draw);
            Assert.Equal(-1, SelectedRuler());
            Assert.Equal(new[] { 5 }, Selected());

            imgui.Key(ImGuiKey.Escape, draw);
            Assert.Empty(Selected());
            Assert.Equal(1, Rulers());
        }

        [Fact]
        public void ARulerHalfPlaced_IsDropped_WhenTheViewGoesUndrawn()
        {
            imgui.MoveMouse(contacts[0].Center, draw);
            imgui.Key(ImGuiKey.R, draw);
            Click(0);

            imgui.Frame(() => { });
            Click(18);

            Assert.Equal(0, Rulers());
            Assert.Equal(new[] { 18 }, Selected());
        }

        [Fact]
        public void WithoutSelection_RulersStillWork()
        {
            selectionEnabled = false;

            PlaceRuler(0, 18);
            Click(8);
            imgui.Key(ImGuiKey.Delete, draw);

            Assert.Equal(0, Rulers());
        }

        [Fact]
        public void Hovered_SaysWhetherThePointerIsOverTheView()
        {
            imgui.MoveMouse(contacts[0].Center, draw);
            Assert.True(selector.Hovered);

            imgui.MoveMouse(new Vector2(-50, -50), draw);
            Assert.False(selector.Hovered);
        }

        [Fact]
        public void TheHoveredContact_IsTheOneUnderThePointer()
        {
            imgui.MoveMouse(contacts[13].Center, draw);
            Assert.Equal(13, selector.HoveredContactIndex);

            imgui.MoveMouse(new Vector2(contacts[1].Max.X + 20, contacts[1].Center.Y), draw);
            Assert.Equal(-1, selector.HoveredContactIndex);
        }

        [Fact]
        public void Refresh_ClearsTheSelection_AndFollowsTheNewProbe()
        {
            Drag(contacts[4].Center, contacts[9].Center);

            selector.Refresh(Probe(1, 4));

            Assert.Equal(4, selector.Contacts.Count);
            Assert.All(selector.Contacts, c => Assert.Equal(default, c));
        }

        object Ruler() => Private.Get<object>(selector, "ruler");

        int Rulers() => Private.Get<System.Collections.ICollection>(Ruler(), "locked").Count;

        int SelectedRuler() => Private.Get<int>(Ruler(), "selectedIdx");

        bool Placing() => ((ImGuiProbeRuler)Ruler()).Placing;

        // NB: the buttons sit above the view and push it down, so the contacts are found again.
        void ShowRulerControls()
        {
            rulerControls = true;
            imgui.Frames(2, draw);
            for (int i = 0; i < Count; i++)
                contacts[i] = Assert.Single(ImGuiHarness.Rects(Fill(i)));
        }

        void PlaceRuler(int from, int to)
        {
            imgui.MoveMouse(contacts[from].Center, draw);
            imgui.Key(ImGuiKey.R, draw);
            Drag(contacts[from].Center, contacts[to].Center);
        }

        int[] Selected() => Indices(c => c.Selected);

        int[] Indices(Func<ContactState, bool> which) =>
            Enumerable.Range(0, selector.Contacts.Count).Where(i => which(selector.Contacts[i])).ToArray();

        void Click(int contact) => ClickAt(contacts[contact].Center);

        void ClickAt(Vector2 at)
        {
            imgui.MoveMouse(at, draw);
            imgui.Click(ImGuiMouseButton.Left, draw);
        }

        void Drag(Vector2 from, Vector2 to)
        {
            imgui.MoveMouse(from, draw);
            imgui.Button(ImGuiMouseButton.Left, true, draw);
            imgui.MoveMouse(to, draw);
            imgui.Button(ImGuiMouseButton.Left, false, draw);
        }

        /// <summary>
        /// A probe of square contacts 12 µm wide, in columns 32 µm apart and rows 20 µm apart, numbered across each
        /// row from the bottom, inside a contour around them.
        /// </summary>
        static SingleProbeGroup Probe(int columns, int rows)
        {
            const double Width = 12, ColumnPitch = 32, RowPitch = 20, Margin = 20;
            var count = columns * rows;
            var right = (columns - 1) * ColumnPitch + Margin;
            var top = (rows - 1) * RowPitch + Margin;

            // NB: written as a probeinterface file and read back, which is the only way ProbeInterface.NET makes a
            // probe.
            var file = new
            {
                specification = "probeinterface",
                version = "0.3.0",
                probes = new[]
                {
                    new
                    {
                        ndim = 2,
                        si_units = "um",
                        annotations = new { model_name = "test", manufacturer = "test" },
                        contact_annotations = new { },
                        contact_positions = Enumerable.Range(0, count)
                            .Select(i => new[] { i % columns * ColumnPitch, i / columns * RowPitch }).ToArray(),
                        contact_plane_axes = Enumerable.Repeat(new[] { new[] { 1.0, 0 }, new[] { 0, 1.0 } }, count).ToArray(),
                        contact_shapes = Enumerable.Repeat("square", count).ToArray(),
                        contact_shape_params = Enumerable.Repeat(new { width = Width }, count).ToArray(),
                        probe_planar_contour = new[]
                        {
                            new[] { -Margin, -Margin }, new[] { right, -Margin }, new[] { right, top }, new[] { -Margin, top },
                        },
                        device_channel_indices = Enumerable.Range(0, count).ToArray(),
                        contact_ids = Enumerable.Range(0, count).Select(i => i.ToString()).ToArray(),
                        shank_ids = Enumerable.Repeat("", count).ToArray(),
                    },
                },
            };
            return JsonConvert.DeserializeObject<TestProbeGroup>(JsonConvert.SerializeObject(file))!;
        }

        sealed class TestProbeGroup : SingleProbeGroup
        {
            [JsonConstructor]
            public TestProbeGroup(string specification, string version, IEnumerable<Probe> probes)
                : base(specification, version, probes)
            {
            }
        }
    }
}
