using AwesomeAssertions;
using Bunit;
using MudBlazor.UnitTests.TestComponents.Table;
using NUnit.Framework;

namespace MudBlazor.UnitTests.Components
{
    /// <summary>
    /// <see cref="MudTable{T}.AutoReloadOnCollectionChanged"/> and <see cref="MudTable{T}.AutoReloadOnItemPropertyChanged"/>:
    /// opt-in re-rendering from INotifyCollectionChanged / INotifyPropertyChanged, parity with MudDataGrid (#11822).
    /// </summary>
    [TestFixture]
    public class TableAutoReloadTests : BunitTest
    {
        private static int RowCount(IRenderedComponent<TableAutoReloadTest> comp) => comp.FindAll("tbody tr.mud-table-row").Count;

        [Test]
        public void Default_DoesNotSubscribe_AndDoesNotRerenderOnAdd()
        {
            var comp = Context.Render<TableAutoReloadTest>();
            var source = comp.Instance.Source;
            RowCount(comp).Should().Be(3);
            source.SubscriberCount.Should().Be(0, "both flags default to false → upstream behaviour is unchanged");
            source[0].SubscriberCount.Should().Be(0);

            var renders = comp.RenderCount;
            source.Add(new TableAutoReloadTest.Item("d"));
            Thread.Sleep(50);
            comp.RenderCount.Should().Be(renders);
            RowCount(comp).Should().Be(3, "without opt-in the table only repaints when its parent re-renders");
        }

        [Test]
        public void CollectionChanged_On_RerendersOnAddRemoveClear()
        {
            var comp = Context.Render<TableAutoReloadTest>(p => p.Add(x => x.AutoReloadOnCollectionChanged, true));
            var source = comp.Instance.Source;
            source.SubscriberCount.Should().Be(1);

            source.Add(new TableAutoReloadTest.Item("d"));
            comp.WaitForAssertion(() => RowCount(comp).Should().Be(4));

            source.RemoveAt(0);
            comp.WaitForAssertion(() => RowCount(comp).Should().Be(3));
            comp.Markup.Should().NotContain(">a<");

            source.Clear();
            comp.WaitForAssertion(() => RowCount(comp).Should().Be(0));
        }

        [Test]
        public void CollectionChanged_On_DoesNotObserveItemProperties()
        {
            var comp = Context.Render<TableAutoReloadTest>(p => p.Add(x => x.AutoReloadOnCollectionChanged, true));
            var item = comp.Instance.Source[0];
            item.SubscriberCount.Should().Be(0);

            var renders = comp.RenderCount;
            item.Name = "changed";
            Thread.Sleep(50);
            comp.RenderCount.Should().Be(renders);
        }

        [Test]
        public void ItemPropertyChanged_On_RerendersOnPropertyChange()
        {
            var comp = Context.Render<TableAutoReloadTest>(p => p.Add(x => x.AutoReloadOnItemPropertyChanged, true));
            var item = comp.Instance.Source[1];
            item.SubscriberCount.Should().Be(1);

            item.Name = "renamed";
            comp.WaitForAssertion(() => comp.Markup.Should().Contain("renamed"));
        }

        [Test]
        public void ItemPropertyChanged_On_ObservesOnlyRenderedRows_AndFollowsRowLifecycle()
        {
            var comp = Context.Render<TableAutoReloadTest>(p => p
                .Add(x => x.AutoReloadOnCollectionChanged, true)
                .Add(x => x.AutoReloadOnItemPropertyChanged, true));
            var source = comp.Instance.Source;

            // A row exists per item on the page → each is observed once.
            source.Should().OnlyContain(i => i.SubscriberCount == 1);

            // Added item: observed as soon as its row renders …
            var added = new TableAutoReloadTest.Item("d");
            source.Add(added);
            comp.WaitForAssertion(() => added.SubscriberCount.Should().Be(1));
            added.Name = "d2";
            comp.WaitForAssertion(() => comp.Markup.Should().Contain("d2"));

            // … removed item: its row is disposed and the subscription goes with it.
            var removed = source[0];
            source.RemoveAt(0);
            comp.WaitForAssertion(() => removed.SubscriberCount.Should().Be(0));
            RowCount(comp).Should().Be(3);
        }

        [Test]
        public void ItemPropertyChanged_On_RerendersOnlyThatRow()
        {
            var comp = Context.Render<TableAutoReloadTest>(p => p.Add(x => x.AutoReloadOnItemPropertyChanged, true));
            var rows = comp.FindComponents<MudTr>();
            Thread.Sleep(200);                       // let the table finish its own post-render settling
            var before = rows.Select(r => r.RenderCount).ToArray();

            comp.Instance.Source[2].Name = "only-me";
            comp.WaitForAssertion(() => comp.Markup.Should().Contain("only-me"));

            // bUnit bumps RenderCount on every ancestor whose markup changed, so the table's count is not
            // evidence either way; sibling rows are — they must not have been rendered again.
            rows[0].RenderCount.Should().Be(before[0]);
            rows[1].RenderCount.Should().Be(before[1]);
            rows[2].RenderCount.Should().BeGreaterThan(before[2]);
        }

        [Test]
        public void ItemPropertyChanged_Burst_IsCoalescedPerRow()
        {
            var comp = Context.Render<TableAutoReloadTest>(p => p.Add(x => x.AutoReloadOnItemPropertyChanged, true));
            var item = comp.Instance.Source[0];
            var renders = comp.RenderCount;

            comp.InvokeAsync(() =>
            {
                for (var i = 0; i < 500; i++)
                {
                    item.Name = $"n{i}";
                }
            });

            comp.WaitForAssertion(() => comp.Markup.Should().Contain("n499"));
            (comp.RenderCount - renders).Should().BeLessThan(10);
        }

        [Test]
        public void Burst_IsCoalescedIntoFewRenders()
        {
            var comp = Context.Render<TableAutoReloadTest>(p => p.Add(x => x.AutoReloadOnCollectionChanged, true));
            var source = comp.Instance.Source;
            var renders = comp.RenderCount;

            comp.InvokeAsync(() =>
            {
                for (var i = 0; i < 500; i++)
                {
                    source.Add(new TableAutoReloadTest.Item($"i{i}"));
                }
            });

            comp.WaitForAssertion(() => RowCount(comp).Should().Be(503));
            (comp.RenderCount - renders).Should().BeLessThan(10, "500 notifications must not schedule 500 renders");
        }

        [Test]
        public async Task Items_Swapped_MovesSubscriptionsToNewCollection()
        {
            var comp = Context.Render<TableAutoReloadTest>(p => p
                .Add(x => x.AutoReloadOnCollectionChanged, true)
                .Add(x => x.AutoReloadOnItemPropertyChanged, true));
            var old = comp.Instance.Source;
            old.SubscriberCount.Should().Be(1);
            old[0].SubscriberCount.Should().Be(1);

            var next = new TableAutoReloadTest.TrackedCollection { new TableAutoReloadTest.Item("x") };
            await comp.SetParametersAndRenderAsync(p => p.Add(x => x.Source, next));

            old.SubscriberCount.Should().Be(0);
            old[0].SubscriberCount.Should().Be(0);
            next.SubscriberCount.Should().Be(1);
            next[0].SubscriberCount.Should().Be(1);
            RowCount(comp).Should().Be(1);
        }

        [Test]
        public async Task Flags_TurnedOff_Unsubscribe()
        {
            var comp = Context.Render<TableAutoReloadTest>(p => p
                .Add(x => x.AutoReloadOnCollectionChanged, true)
                .Add(x => x.AutoReloadOnItemPropertyChanged, true));
            var source = comp.Instance.Source;

            await comp.SetParametersAndRenderAsync(p => p
                .Add(x => x.AutoReloadOnCollectionChanged, false)
                .Add(x => x.AutoReloadOnItemPropertyChanged, false));

            source.SubscriberCount.Should().Be(0);
            source.Should().OnlyContain(i => i.SubscriberCount == 0);
        }

        [Test]
        public async Task Dispose_Unsubscribes_AndLateNotificationsAreIgnored()
        {
            var comp = Context.Render<TableAutoReloadTest>(p => p
                .Add(x => x.AutoReloadOnCollectionChanged, true)
                .Add(x => x.AutoReloadOnItemPropertyChanged, true));
            var source = comp.Instance.Source;
            source.SubscriberCount.Should().Be(1);

            await Context.DisposeComponentsAsync();

            source.SubscriberCount.Should().Be(0);
            source.Should().OnlyContain(i => i.SubscriberCount == 0);
            var act = () => { source.Add(new TableAutoReloadTest.Item("late")); source[0].Name = "late"; };
            act.Should().NotThrow();
        }

        [Test]
        public void Clear_DisposesRows_AndReleasesItemSubscriptions()
        {
            var comp = Context.Render<TableAutoReloadTest>(p => p
                .Add(x => x.AutoReloadOnCollectionChanged, true)
                .Add(x => x.AutoReloadOnItemPropertyChanged, true));
            var source = comp.Instance.Source;
            var items = source.ToList();
            items.Should().OnlyContain(i => i.SubscriberCount == 1);

            source.Clear();   // Reset: no OldItems — the row lifecycle, not event args, releases the subscriptions

            comp.WaitForAssertion(() => RowCount(comp).Should().Be(0));
            comp.WaitForAssertion(() => items.Should().OnlyContain(i => i.SubscriberCount == 0));
        }
    }
}
