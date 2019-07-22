// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Resources;
using Xunit;

namespace System.Windows.Forms.Tests.Serialization
{
    public class SerializableAttributeTests
    {
        [Fact]
        public void EnsureSerializableAttribute()
        {
            BinarySerialization.EnsureSerializableAttribute(
                typeof(Cursor).Assembly,
                new Dictionary<string, bool>
                {
                    // Following classes are participating in resx serialization scenarios.
                    { typeof(ResXDataNode).FullName,  false },
                    { typeof(ResXFileRef).FullName,  false },
                    { typeof(Cursor).FullName,  false },
                    { typeof(ImageListStreamer).FullName,  false },
                    { typeof(ListViewGroup).FullName,  false },
                    { typeof(ListViewItem).FullName,  false },
                    { typeof(ListViewItem.ListViewSubItem).FullName,  false },
                    { "System.Windows.Forms.ListViewItem+ListViewSubItem+SubItemStyle",  false },  // Private type.
                    { typeof(OwnerDrawPropertyBag).FullName,  false },
                    { typeof(TreeNode).FullName,  false },
                    { typeof(TableLayoutSettings).FullName,  false },
                    { typeof(AxHost.State).FullName,  false },
                    { typeof(Padding).FullName,  false },
                    { typeof(LinkArea).FullName,  false },
                    // This class is defined by CoreFx, we own only a partial implementation of it.
                    { "System.LocalAppContext+<>c",  false }
                });
        }
    }
}
