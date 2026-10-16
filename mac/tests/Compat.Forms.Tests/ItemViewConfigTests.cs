using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;
using cYo.Common.Windows.Forms;
using Xunit;

namespace Compat.Forms.Tests
{
	/// <summary>
	/// The real ItemViewConfig is serialized inside DisplayListConfig and StacksConfig in ComicDb.xml. The M0.3b stub
	/// dropped every element but four; the real class must keep them (SortOrder comes from the shim).
	/// </summary>
	public class ItemViewConfigTests
	{
		// A reduced excerpt of a <View> element of the owner's fixture ComicDb.xml.
		private const string Xml = @"<ItemViewConfig ItemViewMode=""Thumbnail"" Grouping=""true"" SortKey=""Series"" GrouperId=""g1"" StackerId=""s1"" ItemSortOrder=""Descending"">
  <Columns>
    <Column Id=""63"" Visible=""false"" Width=""100"" />
    <Column Id=""200"" Visible=""false"" Width=""50"" />
    <Column Id=""71"" Visible=""false"" />
  </Columns>
  <GroupsStatus>
    <Status>AllExpanded</Status>
    <Keys />
  </GroupsStatus>
  <ThumbnailSize>
    <Width>120</Width>
    <Height>180</Height>
  </ThumbnailSize>
  <TileSize>
    <Width>300</Width>
    <Height>200</Height>
  </TileSize>
  <ItemRowHeight>22</ItemRowHeight>
</ItemViewConfig>";

		private static ItemViewConfig Read(string xml)
		{
			return (ItemViewConfig)new XmlSerializer(typeof(ItemViewConfig)).Deserialize(new StringReader(xml));
		}

		[Fact]
		public void Reads_all_the_view_settings_that_the_stub_dropped()
		{
			ItemViewConfig config = Read(Xml);
			Assert.Equal(ItemViewMode.Thumbnail, config.ItemViewMode);
			Assert.True(config.Grouping);
			Assert.Equal("Series", config.SortKey);
			Assert.Equal("g1", config.GrouperId);
			Assert.Equal("s1", config.StackerId);
			Assert.Equal(SortOrder.Descending, config.ItemSortOrder);
			Assert.Equal(SortOrder.Ascending, config.GroupSortOrder);
			Assert.Equal(new[] { 63, 200, 71 }, config.Columns.Select(c => c.Id).ToArray());
			Assert.Equal(new[] { 100, 50, 80 }, config.Columns.Select(c => c.Width).ToArray());
			Assert.All(config.Columns, c => Assert.False(c.Visible));
			Assert.Equal(ItemViewGroupsStatus.GroupStatus.AllExpanded, config.GroupsStatus.Status);
			Assert.Equal(new System.Drawing.Size(120, 180), config.ThumbnailSize);
			Assert.Equal(new System.Drawing.Size(300, 200), config.TileSize);
			Assert.Equal(22, config.ItemRowHeight);
		}

		[Fact]
		public void Round_trips_without_losing_anything()
		{
			ItemViewConfig first = Read(Xml);
			var serializer = new XmlSerializer(typeof(ItemViewConfig));
			var writer = new StringWriter();
			serializer.Serialize(writer, first);
			ItemViewConfig second = Read(writer.ToString());
			var again = new StringWriter();
			serializer.Serialize(again, second);
			Assert.Equal(writer.ToString(), again.ToString());
			Assert.Equal(first.ThumbnailSize, second.ThumbnailSize);
			Assert.Equal(first.ItemSortOrder, second.ItemSortOrder);
			Assert.Equal(first.Columns.Count, second.Columns.Count);
		}

		[Fact]
		public void Defaults_are_the_windows_defaults()
		{
			var config = new ItemViewConfig();
			Assert.Equal(ItemViewMode.Detail, config.ItemViewMode);
			Assert.Equal(SortOrder.Ascending, config.ItemSortOrder);
			Assert.Equal(SortOrder.Ascending, config.GroupSortOrder);
			Assert.Empty(config.Columns);
		}
	}
}
