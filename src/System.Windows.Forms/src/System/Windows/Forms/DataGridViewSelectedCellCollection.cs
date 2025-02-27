﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.ComponentModel;
using System.Diagnostics;

namespace System.Windows.Forms
{
    /// <summary>
    /// Represents a collection of selected <see cref='DataGridViewCell'/> objects in the <see cref='DataGridView'/>
    /// control.
    /// </summary>
    [ListBindable(false)]
    public class DataGridViewSelectedCellCollection : BaseCollection, IList
    {
        readonly ArrayList items = new ArrayList();

        int IList.Add(object value)
        {
            throw new NotSupportedException(string.Format(SR.DataGridView_ReadOnlyCollection));
        }

        void IList.Clear()
        {
            throw new NotSupportedException(string.Format(SR.DataGridView_ReadOnlyCollection));
        }

        bool IList.Contains(object value)
        {
            return items.Contains(value);
        }

        int IList.IndexOf(object value)
        {
            return items.IndexOf(value);
        }

        void IList.Insert(int index, object value)
        {
            throw new NotSupportedException(string.Format(SR.DataGridView_ReadOnlyCollection));
        }

        void IList.Remove(object value)
        {
            throw new NotSupportedException(string.Format(SR.DataGridView_ReadOnlyCollection));
        }

        void IList.RemoveAt(int index)
        {
            throw new NotSupportedException(string.Format(SR.DataGridView_ReadOnlyCollection));
        }

        bool IList.IsFixedSize
        {
            get { return true; }
        }

        bool IList.IsReadOnly
        {
            get { return true; }
        }

        object IList.this[int index]
        {
            get { return items[index]; }
            set { throw new NotSupportedException(string.Format(SR.DataGridView_ReadOnlyCollection)); }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            items.CopyTo(array, index);
        }

        int ICollection.Count
        {
            get { return items.Count; }
        }

        bool ICollection.IsSynchronized
        {
            get { return false; }
        }

        object ICollection.SyncRoot
        {
            get { return this; }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return items.GetEnumerator();
        }

        internal DataGridViewSelectedCellCollection()
        {
        }

        protected override ArrayList List
        {
            get
            {
                return items;
            }
        }

        public DataGridViewCell this[int index]
        {
            get
            {
                return (DataGridViewCell)items[index];
            }
        }

        /// <summary>
        /// Adds a <see cref='DataGridViewCell'/> to this collection.
        /// </summary>
        internal int Add(DataGridViewCell dataGridViewCell)
        {
            Debug.Assert(!Contains(dataGridViewCell));
            return items.Add(dataGridViewCell);
        }

        /* Not used for now
        internal void AddRange(DataGridViewCell[] dataGridViewCells)
        {
            Debug.Assert(dataGridViewCells != null);
            foreach(DataGridViewCell dataGridViewCell in dataGridViewCells)
            {
                Debug.Assert(!Contains(dataGridViewCell));
                this.items.Add(dataGridViewCell);
            }
        }

        internal void AddCellCollection(DataGridViewSelectedCellCollection dataGridViewCells)
        {
            Debug.Assert(dataGridViewCells != null);
            foreach(DataGridViewCell dataGridViewCell in dataGridViewCells)
            {
                Debug.Assert(!Contains(dataGridViewCell));
                this.items.Add(dataGridViewCell);
            }
        }
        */

        /// <summary>
        /// Adds all the <see cref='DataGridViewCell'/> objects from the provided linked list to this collection.
        /// </summary>
        internal void AddCellLinkedList(DataGridViewCellLinkedList dataGridViewCells)
        {
            Debug.Assert(dataGridViewCells != null);
            foreach (DataGridViewCell dataGridViewCell in dataGridViewCells)
            {
                Debug.Assert(!Contains(dataGridViewCell));
                items.Add(dataGridViewCell);
            }
        }

        [
            EditorBrowsable(EditorBrowsableState.Never)
        ]
        public void Clear()
        {
            throw new NotSupportedException(string.Format(SR.DataGridView_ReadOnlyCollection));
        }

        /// <summary>
        ///  Checks to see if a DataGridViewCell is contained in this collection.
        /// </summary>
        public bool Contains(DataGridViewCell dataGridViewCell)
        {
            return items.IndexOf(dataGridViewCell) != -1;
        }

        public void CopyTo(DataGridViewCell[] array, int index)
        {
            items.CopyTo(array, index);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Insert(int index, DataGridViewCell dataGridViewCell)
        {
            throw new NotSupportedException(string.Format(SR.DataGridView_ReadOnlyCollection));
        }
    }
}
