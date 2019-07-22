﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.CodeDom;
using System.Runtime.Serialization;

namespace System.ComponentModel.Design.Serialization
{
    /// <summary>
    /// The exception that is thrown when the code dom serializer experiences an error.
    /// </summary>
    [Serializable] // Exceptions should exchange successfully between the classic and Core frameworks.
    // Note: This class implements ISerializable and uses hardcoded strings to store member data. 
    // It is safe to change member names, but not the serialization key values.
    public class CodeDomSerializerException : SystemException
    {
        public CodeDomSerializerException(string message, CodeLinePragma linePragma) : base(message)
        {
            LinePragma = linePragma;
        }

        public CodeDomSerializerException(Exception ex, CodeLinePragma linePragma) : base(ex?.Message, ex)
        {
            LinePragma = linePragma;
        }

        public CodeDomSerializerException(string message, IDesignerSerializationManager manager) : base(message)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
        }

        public CodeDomSerializerException(Exception ex, IDesignerSerializationManager manager) : base(ex?.Message, ex)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
        }

        protected CodeDomSerializerException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            LinePragma = (CodeLinePragma)info.GetValue("linePragma", typeof(CodeLinePragma));
        }

        /// <summary>
        /// Gets the line pragma object that is related to this error.
        /// </summary>
        public CodeLinePragma LinePragma { get; }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue("linePragma", LinePragma);
            base.GetObjectData(info, context);
        }
    }
}
