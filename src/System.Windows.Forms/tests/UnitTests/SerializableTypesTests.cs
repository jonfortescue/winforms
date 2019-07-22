// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Drawing;
using Xunit;

namespace System.Windows.Forms.Tests.Serialization
{
    public class SerializableTypesTests
    {
        [Fact]
        public void TreeNodeAndPropertyBag_RoundTrip()
        {
            var children = new TreeNode[] { new TreeNode("node2"), new TreeNode("node3") };
            TreeNode treeNodeIn = new TreeNode("node1", 1, 2, children)
            {
                ToolTipText = "tool tip text",
                Name = "node1",
                SelectedImageKey = "key",
                Checked = true,
                BackColor = Color.Yellow, // Colors and Font are serialized into the property bag.
                ForeColor = Color.Green,
                NodeFont = new Font(FontFamily.GenericSansSerif, 9f)
            };

            var coreBlob = BinarySerialization.ToBase64String(treeNodeIn);
            Assert.Equal(ClassicTreeNode, coreBlob);

            var treeNode = BinarySerialization.EnsureDeserialize(coreBlob) as TreeNode;

            Assert.NotNull(treeNode);
            Assert.Equal("node1", treeNode.Text);
            Assert.Equal(-1, treeNode.ImageIndex); // No image list
            Assert.Equal("key", treeNode.SelectedImageKey);
            Assert.Equal(2, treeNode.childCount);
            Assert.Equal("node2", treeNode.FirstNode.Text);
            Assert.Equal("node3", treeNode.LastNode.Text);
            Assert.Equal("tool tip text", treeNode.ToolTipText);
            Assert.Equal("node1", treeNode.Name);
            Assert.True(treeNode.Checked);

            Assert.Equal(Color.Yellow, treeNode.BackColor);
            Assert.Equal(Color.Green, treeNode.ForeColor);
            Assert.Equal(FontFamily.GenericSansSerif.Name, treeNode.NodeFont.FontFamily.Name);
        }

        [Fact]
        public void TableLayoutSettings_RoundTrip()
        {
            string coreBlob;

            using (var toolStrip = new ToolStrip { LayoutStyle = ToolStripLayoutStyle.Table })
            {
                var tableSettingsIn = toolStrip.LayoutSettings as TableLayoutSettings;
                tableSettingsIn.ColumnCount = 2;
                tableSettingsIn.RowCount = 1;
                var bitmap = new Bitmap(10, 20);

                for (int i = 0; i < tableSettingsIn.ColumnCount; i++)
                {
                    for (int j = 0; j < tableSettingsIn.RowCount; j++)
                    {
                        var button = new ToolStripButton
                        {
                            DisplayStyle = ToolStripItemDisplayStyle.Image,
                            Image = bitmap,
                            ImageAlign = ContentAlignment.MiddleCenter,
                            ImageScaling = ToolStripItemImageScaling.None,
                            Margin = Padding.Empty,
                            Padding = Padding.Empty
                        };

                        toolStrip.Items.Add(button);

                        var cellPosition = new TableLayoutPanelCellPosition(i, j);
                        tableSettingsIn.SetCellPosition(button, cellPosition);
                    }
                }

                coreBlob = BinarySerialization.ToBase64String(tableSettingsIn);
            }

            Assert.Equal(ClassicTableLayoutSettings, coreBlob);

            var tableSettings = BinarySerialization.EnsureDeserialize(coreBlob) as TableLayoutSettings;
            Assert.NotNull(tableSettings);
        }

        public const string ClassicTreeNode =
            "AAEAAAD/////AQAAAAAAAAAMAgAAAFdTeXN0ZW0uV2luZG93cy5Gb3JtcywgVmVyc2lvbj00LjAuMC4wLCBDdWx0dXJlPW5ldXRyYWwsIFB1YmxpY0tleVRva2VuPWI3N2E1YzU2MTkzNGUwODkFAQAAAB1TeXN0ZW0uV2luZG93cy5Gb3Jtcy5UcmVlTm9kZQwAAAAHUHJvcEJhZwRUZXh0C1Rvb2xUaXBUZXh0BE5hbWUJSXNDaGVja2VkCkltYWdlSW5kZXgISW1hZ2VLZXkSU2VsZWN0ZWRJbWFnZUluZGV4EFNlbGVjdGVkSW1hZ2VLZXkKQ2hpbGRDb3VudAljaGlsZHJlbjAJY2hpbGRyZW4xBAEBAQAAAQABAAQEKVN5c3RlbS5XaW5kb3dzLkZvcm1zLk93bmVyRHJhd1Byb3BlcnR5QmFnAgAAAAEICAgdU3lzdGVtLldpbmRvd3MuRm9ybXMuVHJlZU5vZGUCAAAAHVN5c3RlbS5XaW5kb3dzLkZvcm1zLlRyZWVOb2RlAgAAAAIAAAAJAwAAAAYEAAAABW5vZGUxBgUAAAANdG9vbCB0aXAgdGV4dAkEAAAAAQEAAAAGBwAAAAD/////BggAAAADa2V5AgAAAAkJAAAACQoAAAAMCwAAAFFTeXN0ZW0uRHJhd2luZywgVmVyc2lvbj00LjAuMC4wLCBDdWx0dXJlPW5ldXRyYWwsIFB1YmxpY0tleVRva2VuPWIwM2Y1ZjdmMTFkNTBhM2EFAwAAAClTeXN0ZW0uV2luZG93cy5Gb3Jtcy5Pd25lckRyYXdQcm9wZXJ0eUJhZwMAAAAJQmFja0NvbG9yCUZvcmVDb2xvcgRGb250BAQEFFN5c3RlbS5EcmF3aW5nLkNvbG9yCwAAABRTeXN0ZW0uRHJhd2luZy5Db2xvcgsAAAATU3lzdGVtLkRyYXdpbmcuRm9udAsAAAACAAAABfT///8UU3lzdGVtLkRyYXdpbmcuQ29sb3IEAAAABG5hbWUFdmFsdWUKa25vd25Db2xvcgVzdGF0ZQEAAAAJBwcLAAAACgAAAAAAAAAApgABAAHz////9P///woAAAAAAAAAAE8AAQAJDgAAAAUJAAAAHVN5c3RlbS5XaW5kb3dzLkZvcm1zLlRyZWVOb2RlCQAAAARUZXh0C1Rvb2xUaXBUZXh0BE5hbWUJSXNDaGVja2VkCkltYWdlSW5kZXgISW1hZ2VLZXkSU2VsZWN0ZWRJbWFnZUluZGV4EFNlbGVjdGVkSW1hZ2VLZXkKQ2hpbGRDb3VudAEBAQAAAQABAAEICAgCAAAABg8AAAAFbm9kZTIJBwAAAAkHAAAAAP////8JBwAAAP////8JBwAAAAAAAAAFCgAAAB1TeXN0ZW0uV2luZG93cy5Gb3Jtcy5UcmVlTm9kZQkAAAAEVGV4dAtUb29sVGlwVGV4dAROYW1lCUlzQ2hlY2tlZApJbWFnZUluZGV4CEltYWdlS2V5ElNlbGVjdGVkSW1hZ2VJbmRleBBTZWxlY3RlZEltYWdlS2V5CkNoaWxkQ291bnQBAQEAAAEAAQABCAgIAgAAAAYRAAAABW5vZGUzCQcAAAAJBwAAAAD/////CQcAAAD/////CQcAAAAAAAAABQ4AAAATU3lzdGVtLkRyYXdpbmcuRm9udAQAAAAETmFtZQRTaXplBVN0eWxlBFVuaXQBAAQECxhTeXN0ZW0uRHJhd2luZy5Gb250U3R5bGULAAAAG1N5c3RlbS5EcmF3aW5nLkdyYXBoaWNzVW5pdAsAAAALAAAABhMAAAAUTWljcm9zb2Z0IFNhbnMgU2VyaWYAABBBBez///8YU3lzdGVtLkRyYXdpbmcuRm9udFN0eWxlAQAAAAd2YWx1ZV9fAAgLAAAAAAAAAAXr////G1N5c3RlbS5EcmF3aW5nLkdyYXBoaWNzVW5pdAEAAAAHdmFsdWVfXwAICwAAAAMAAAAL";

        public const string ClassicTableLayoutSettings =
            "AAEAAAD/////AQAAAAAAAAAMAgAAAFdTeXN0ZW0uV2luZG93cy5Gb3JtcywgVmVyc2lvbj00LjAuMC4wLCBDdWx0dXJlPW5ldXRyYWwsIFB1YmxpY0tleVRva2VuPWI3N2E1YzU2MTkzNGUwODkFAQAAAChTeXN0ZW0uV2luZG93cy5Gb3Jtcy5UYWJsZUxheW91dFNldHRpbmdzAQAAABBTZXJpYWxpemVkU3RyaW5nAQIAAAAGAwAAAIUBPD94bWwgdmVyc2lvbj0iMS4wIiBlbmNvZGluZz0idXRmLTE2Ij8+PFRhYmxlTGF5b3V0U2V0dGluZ3M+PENvbnRyb2xzIC8+PENvbHVtbnMgU3R5bGVzPSIiIC8+PFJvd3MgU3R5bGVzPSIiIC8+PC9UYWJsZUxheW91dFNldHRpbmdzPgs=";

    }
}
