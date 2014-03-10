﻿using System;
using System.Collections.Generic;
using System.IO;

namespace Duality.Serialization
{
	/// <summary>
	/// Base class for Dualitys binary serializers.
	/// </summary>
	public abstract class BinaryFormatterBase : Formatter
	{
		/// <summary>
		/// Buffer object for <see cref="Duality.Serialization.ISerializeExplicit">custom de/serialization</see>, 
		/// providing read and write functionality.
		/// </summary>
		protected class CustomSerialIO : CustomSerialIOBase<BinaryFormatterBase>
		{
			/// <summary>
			/// Writes the contained data to the specified serializer.
			/// </summary>
			/// <param name="formatter">The serializer to write data to.</param>
			public void Serialize(BinaryFormatterBase formatter)
			{
				formatter.WritePrimitive(this.data.Count);
				foreach (var pair in this.data)
				{
					formatter.WriteString(pair.Key);
					formatter.WriteObjectData(pair.Value);
				}
				this.Clear();
			}
			/// <summary>
			/// Reads data from the specified serializer
			/// </summary>
			/// <param name="formatter">The serializer to read data from.</param>
			public void Deserialize(BinaryFormatterBase formatter)
			{
				this.Clear();
				int count = (int)formatter.ReadPrimitive(DataType.Int);
				for (int i = 0; i < count; i++)
				{
					string key = formatter.ReadString();
					object value = formatter.ReadObjectData();
					this.data.Add(key, value);
				}
			}
		}


		protected	const	string	HeaderId	= "BinaryFormatterHeader";
		protected	const	ushort	Version		= 3;


		protected	BinaryWriter	writer		= null;
		protected	BinaryReader	reader		= null;
		protected	ushort			dataVersion	= 0;

		private		Stack<long>							offsetStack			= new Stack<long>();
		private		Dictionary<string,TypeDataLayout>	typeDataLayout		= new Dictionary<string,TypeDataLayout>();
		private		Dictionary<string,long>				typeDataLayoutMap	= new Dictionary<string,long>();

		
		public override bool CanWrite
		{
			get { return this.writer != null; }
		}
		public override bool CanRead
		{
			get { return this.reader != null; }
		}


		protected BinaryFormatterBase() : this(null) {}
		protected BinaryFormatterBase(Stream stream)
		{
			stream = stream.NonClosing();
			this.writer = (stream != null && stream.CanWrite) ? new BinaryWriter(stream) : null;
			this.reader = (stream != null && stream.CanRead) ? new BinaryReader(stream) : null;
		}
		protected override void OnDisposed(bool manually)
		{
			base.OnDisposed(manually);

			if (this.writer != null)
			{
				this.writer.Flush();
				this.writer.Dispose();
				this.writer = null;
			}

			if (this.reader != null)
			{
				this.reader.Dispose();
				this.reader = null;
			}
		}

		protected override object ReadObjectData()
		{
			if (this.reader.BaseStream.Position == this.reader.BaseStream.Length) throw new EndOfStreamException("No more data to read.");

			// Not null flag
			bool isNotNull = this.reader.ReadBoolean();
			if (!isNotNull) return this.GetNullObject();

			// Read data type header
			DataType dataType = this.ReadDataType();
			long lastPos = this.reader.BaseStream.Position;
			long offset = this.reader.ReadInt64();

			// Read object
			object result = null;
			try
			{
				// Read the objects body
				result = this.ReadObjectBody(dataType);

				// If we read the object properly and aren't where we're supposed to be, something went wrong
				if (this.reader.BaseStream.Position != lastPos + offset) throw new ApplicationException(string.Format("Wrong dataset offset: '{0}' instead of expected value '{1}'.", this.reader.BaseStream.Position - lastPos, offset));
			}
			catch (Exception e)
			{
				// If anything goes wrong, assure the stream position is valid and points to the next data entry
				this.reader.BaseStream.Seek(lastPos + offset, SeekOrigin.Begin);
				// Log the error
				this.SerializationLog.WriteError("Error reading object at '{0:X8}'-'{1:X8}': {2}", 
					lastPos,
					lastPos + offset, 
					e is ApplicationException ? e.Message : Log.Exception(e));
			}

			return result ?? this.GetNullObject();
		}
		/// <summary>
		/// Reads the body of an object.
		/// </summary>
		/// <param name="dataType">The <see cref="Duality.Serialization.DataType"/> that is assumed.</param>
		/// <returns>The object that has been read.</returns>
		protected abstract object ReadObjectBody(DataType dataType);

		/// <summary>
		/// Returns the cached version of the specified typenames <see cref="Duality.Serialization.TypeDataLayout"/>.
		/// </summary>
		/// <param name="t">A string referring to the <see cref="System.Type"/> that is described by the 
		/// <see cref="Duality.Serialization.TypeDataLayout"/> in question.</param>
		/// <returns></returns>
		protected TypeDataLayout GetCachedTypeDataLayout(string t)
		{
			TypeDataLayout result;
			if (!this.typeDataLayout.TryGetValue(t, out result)) return null;
			return result;
		}
		/// <summary>
		/// Reads the <see cref="Duality.Serialization.TypeDataLayout"/> describing the specified <see cref="System.Type"/>.
		/// </summary>
		/// <param name="t">The <see cref="System.Type"/> of which to read the <see cref="Duality.Serialization.TypeDataLayout"/>.</param>
		/// <returns>A <see cref="Duality.Serialization.TypeDataLayout"/> describing the specified <see cref="System.Type"/></returns>
		protected TypeDataLayout ReadTypeDataLayout(Type t)
		{
			return this.ReadTypeDataLayout(t.GetSerializeType().TypeString);
		}
		/// <summary>
		/// Reads the <see cref="Duality.Serialization.TypeDataLayout"/> describing the specified <see cref="System.Type"/>.
		/// </summary>
		/// <param name="t">A string referring to the <see cref="System.Type"/> of which to read the <see cref="Duality.Serialization.TypeDataLayout"/>.</param>
		/// <returns>A <see cref="Duality.Serialization.TypeDataLayout"/> describing the specified <see cref="System.Type"/></returns>
		protected TypeDataLayout ReadTypeDataLayout(string t)
		{
			long backRef = this.reader.ReadInt64();

			TypeDataLayout result;
			if (this.typeDataLayout.TryGetValue(t, out result) && backRef != -1L) return result;

			long lastPos = this.reader.BaseStream.Position;
			if (backRef != -1L) this.reader.BaseStream.Seek(backRef, SeekOrigin.Begin);
			result = result ?? new TypeDataLayout(this.reader);
			if (backRef != -1L) this.reader.BaseStream.Seek(lastPos, SeekOrigin.Begin);

			this.typeDataLayout[t] = result;
			return result;
		}
		/// <summary>
		/// Reads the binary serialization header.
		/// </summary>
		protected void ReadFormatterHeader()
		{
			long initialPos = this.reader.BaseStream.Position;
			try
			{
				string headerId = this.reader.ReadString();
				if (headerId != HeaderId) throw new ApplicationException("Header ID does not match.");
				this.dataVersion = this.reader.ReadUInt16();

				// Create "Safe zone" for additional data
				long lastPos = this.reader.BaseStream.Position;
				long offset = this.reader.ReadInt64();
				try
				{
					// --[ Insert reading additional data here ]--

					// If we read the object properly and aren't where we're supposed to be, something went wrong
					if (this.reader.BaseStream.Position != lastPos + offset) throw new ApplicationException(string.Format("Wrong dataset offset: '{0}' instead of expected value '{1}'.", offset, this.reader.BaseStream.Position - lastPos));
				}
				catch (Exception e)
				{
					// If anything goes wrong, assure the stream position is valid and points to the next data entry
					this.reader.BaseStream.Seek(lastPos + offset, SeekOrigin.Begin);
					this.SerializationLog.WriteError("Error reading header at '{0:X8}'-'{1:X8}': {2}", lastPos, lastPos + offset, Log.Exception(e));
				}
			}
			catch (Exception e) 
			{
				this.reader.BaseStream.Seek(initialPos, SeekOrigin.Begin);
				this.SerializationLog.WriteError("Error reading header: {0}", Log.Exception(e));
			}
		}
		/// <summary>
		/// Reads a single primitive value, assuming the specified <see cref="Duality.Serialization.DataType"/>.
		/// </summary>
		/// <param name="dataType"></param>
		/// <returns></returns>
		protected object ReadPrimitive(DataType dataType)
		{
			switch (dataType)
			{
				case DataType.Bool:			return this.reader.ReadBoolean();
				case DataType.Byte:			return this.reader.ReadByte();
				case DataType.SByte:		return this.reader.ReadSByte();
				case DataType.Short:		return this.reader.ReadInt16();
				case DataType.UShort:		return this.reader.ReadUInt16();
				case DataType.Int:			return this.reader.ReadInt32();
				case DataType.UInt:			return this.reader.ReadUInt32();
				case DataType.Long:			return this.reader.ReadInt64();
				case DataType.ULong:		return this.reader.ReadUInt64();
				case DataType.Float:		return this.reader.ReadSingle();
				case DataType.Double:		return this.reader.ReadDouble();
				case DataType.Decimal:		return this.reader.ReadDecimal();
				case DataType.Char:			return this.reader.ReadChar();
				case DataType.String:		return this.reader.ReadString();
				default:
					throw new ArgumentException(string.Format("DataType '{0}' is not a primitive.", dataType));
			}
		}
		/// <summary>
		/// Reads a single string value.
		/// </summary>
		/// <returns></returns>
		protected string ReadString()
		{
			return this.reader.ReadString();
		}

		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(bool[] obj)
		{
			for (int l = 0; l < obj.Length; l++) obj[l] = this.reader.ReadBoolean();
		}
		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(byte[] obj)
		{
			this.reader.Read(obj, 0, obj.Length);
		}
		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(sbyte[] obj)
		{
			for (int l = 0; l < obj.Length; l++) obj[l] = this.reader.ReadSByte();
		}
		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(short[] obj)
		{
			for (int l = 0; l < obj.Length; l++) obj[l] = this.reader.ReadInt16();
		}
		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(ushort[] obj)
		{
			for (int l = 0; l < obj.Length; l++) obj[l] = this.reader.ReadUInt16();
		}
		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(int[] obj)
		{
			for (int l = 0; l < obj.Length; l++) obj[l] = this.reader.ReadInt32();
		}
		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(uint[] obj)
		{
			for (int l = 0; l < obj.Length; l++) obj[l] = this.reader.ReadUInt32();
		}
		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(long[] obj)
		{
			for (int l = 0; l < obj.Length; l++) obj[l] = this.reader.ReadInt64();
		}
		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(ulong[] obj)
		{
			for (int l = 0; l < obj.Length; l++) obj[l] = this.reader.ReadUInt64();
		}
		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(float[] obj)
		{
			for (int l = 0; l < obj.Length; l++) obj[l] = this.reader.ReadSingle();
		}
		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(double[] obj)
		{
			for (int l = 0; l < obj.Length; l++) obj[l] = this.reader.ReadDouble();
		}
		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(decimal[] obj)
		{
			for (int l = 0; l < obj.Length; l++) obj[l] = this.reader.ReadDecimal();
		}
		/// <summary>
		/// Reads a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(char[] obj)
		{
			for (int l = 0; l < obj.Length; l++) obj[l] = this.reader.ReadChar();
		}
		/// <summary>
		/// Reads a string array.
		/// </summary>
		/// <param name="obj"></param>
		protected void ReadArrayData(string[] obj)
		{
			for (int l = 0; l < obj.Length; l++)
			{
				bool isNotNull = this.reader.ReadBoolean();
				if (isNotNull)
					obj[l] = this.reader.ReadString();
				else
					obj[l] = null;
			}
		}
		
		protected override void WriteObjectData(object obj)
		{
			// NotNull flag
			if (object.Equals(obj, this.GetNullObject()))
			{
				this.writer.Write(false);
				return;
			}
			else
				this.writer.Write(true);
			
			// Retrieve type data
			ObjectHeader header = this.PrepareWriteObject(obj);

			// Write data type header
			this.WriteDataType(header.DataType);
			this.WritePushOffset();
			try
			{
				// Write object
				this.idManager.PushIdLevel();
				this.WriteObjectBody(obj, header);
			}
			finally
			{
				// Write object footer
				this.WritePopOffset();
				this.idManager.PopIdLevel();
			}
		}
		/// <summary>
		/// Writes the body of a given object.
		/// </summary>
		/// <param name="dataType">The <see cref="Duality.Serialization.DataType"/> as which the object will be written.</param>
		/// <param name="obj">The object to be written.</param>
		/// <param name="objSerializeType">The <see cref="Duality.Serialization.SerializeType"/> that describes the specified object.</param>
		/// <param name="objId">An object id that is assigned to the specified object.</param>
		protected abstract void WriteObjectBody(object obj, ObjectHeader header);
		
		/// <summary>
		/// Writes the binary serialization header.
		/// </summary>
		protected void WriteFormatterHeader()
		{
			this.writer.Write(HeaderId);
			this.writer.Write(Version);
			this.WritePushOffset();

			// --[ Insert writing additional header data here ]--

			this.WritePopOffset();
		}
		/// <summary>
		/// Writes the <see cref="Duality.Serialization.TypeDataLayout"/> describing the specified <see cref="Duality.Serialization.SerializeType"/>.
		/// Note that this method does not write redundant layout data - if the specified TypeDataLayout has already been written withing the same
		/// operation, a back-reference is written instead.
		/// </summary>
		/// <param name="objSerializeType">
		/// The <see cref="Duality.Serialization.SerializeType"/> of which to write the <see cref="Duality.Serialization.TypeDataLayout"/>
		/// </param>
		/// <seealso cref="WriteTypeDataLayout(string)"/>
		/// <seealso cref="WriteTypeDataLayout(TypeDataLayout, string)"/>
		protected void WriteTypeDataLayout(SerializeType objSerializeType)
		{
			if (this.typeDataLayout.ContainsKey(objSerializeType.TypeString))
			{
				long backRef = this.typeDataLayoutMap[objSerializeType.TypeString];
				this.writer.Write(backRef);
				return;
			}

			this.WriteTypeDataLayout(new TypeDataLayout(objSerializeType), objSerializeType.TypeString);
		}
		/// <summary>
		/// Writes the <see cref="Duality.Serialization.TypeDataLayout"/> describing the specified <see cref="Duality.Serialization.SerializeType"/>.
		/// Note that this method does not write redundant layout data - if the specified TypeDataLayout has already been written withing the same
		/// operation, a back-reference is written instead.
		/// </summary>
		/// <param name="typeString">
		/// A string referring to the <see cref="System.Type"/> of which to write the <see cref="Duality.Serialization.TypeDataLayout"/>.
		/// </param>
		/// <seealso cref="WriteTypeDataLayout(SerializeType)"/>
		/// <seealso cref="WriteTypeDataLayout(TypeDataLayout, string)"/>
		protected void WriteTypeDataLayout(string typeString)
		{
			if (this.typeDataLayout.ContainsKey(typeString))
			{
				long backRef = this.typeDataLayoutMap[typeString];
				this.writer.Write(backRef);
				return;
			}

			Type resolved = this.ResolveType(typeString);
			SerializeType cached = resolved != null ? resolved.GetSerializeType() : null;
			TypeDataLayout layout = cached != null ? new TypeDataLayout(cached) : null;
			this.WriteTypeDataLayout(layout, typeString);
		}
		/// <summary>
		/// Writes the specified <see cref="Duality.Serialization.TypeDataLayout"/> describing the <see cref="System.Type"/> referred to by
		/// the given <see cref="ReflectionHelper.GetTypeId">type string</see>. Doesn't care about redundant data, writes always.
		/// </summary>
		/// <param name="layout">The <see cref="Duality.Serialization.TypeDataLayout"/> to write.</param>
		/// <param name="typeString">
		/// The <see cref="ReflectionHelper.GetTypeId">type string</see> that refers to the <see cref="System.Type"/> that
		/// is described by the specified <see cref="Duality.Serialization.TypeDataLayout"/>.
		/// </param>
		/// <seealso cref="WriteTypeDataLayout(string)"/>
		/// <seealso cref="WriteTypeDataLayout(SerializeType)"/>
		protected void WriteTypeDataLayout(TypeDataLayout layout, string typeString)
		{
			this.typeDataLayout[typeString] = layout;
			this.writer.Write(-1L);
			this.typeDataLayoutMap[typeString] = this.writer.BaseStream.Position;
			layout.Write(this.writer);
		}
		/// <summary>
		/// Writes a single primitive value.
		/// </summary>
		/// <param name="obj">The primitive value to write.</param>
		protected void WritePrimitive(object obj)
		{
			if		(obj is bool)		this.writer.Write((bool)obj);
			else if (obj is byte)		this.writer.Write((byte)obj);
			else if (obj is char)		this.writer.Write((char)obj);
			else if (obj is string)		this.writer.Write((string)obj);
			else if (obj is sbyte)		this.writer.Write((sbyte)obj);
			else if (obj is short)		this.writer.Write((short)obj);
			else if (obj is ushort)		this.writer.Write((ushort)obj);
			else if (obj is int)		this.writer.Write((int)obj);
			else if (obj is uint)		this.writer.Write((uint)obj);
			else if (obj is long)		this.writer.Write((long)obj);
			else if (obj is ulong)		this.writer.Write((ulong)obj);
			else if (obj is float)		this.writer.Write((float)obj);
			else if (obj is double)		this.writer.Write((double)obj);
			else if (obj is decimal)	this.writer.Write((decimal)obj);
			else if (obj == null)
				throw new ArgumentNullException("obj");
			else
				throw new ArgumentException(string.Format("Type '{0}' is not a primitive.", obj.GetType()));
		}
		/// <summary>
		/// Writes a single string value.
		/// </summary>
		/// <param name="obj">The string value to write.</param>
		protected void WriteString(string obj)
		{
			this.writer.Write(obj);
		}

		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(bool[] obj)
		{
			for (long l = 0; l < obj.Length; l++) this.writer.Write(obj[l]);
		}
		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(byte[] obj)
		{
			this.writer.Write(obj);
		}
		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(sbyte[] obj)
		{
			for (long l = 0; l < obj.Length; l++) this.writer.Write(obj[l]);
		}
		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(short[] obj)
		{
			for (long l = 0; l < obj.Length; l++) this.writer.Write(obj[l]);
		}
		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(ushort[] obj)
		{
			for (int l = 0; l < obj.Length; l++) this.writer.Write(obj[l]);
		}
		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(int[] obj)
		{
			for (int l = 0; l < obj.Length; l++) this.writer.Write(obj[l]);
		}
		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(uint[] obj)
		{
			for (int l = 0; l < obj.Length; l++) this.writer.Write(obj[l]);
		}
		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(long[] obj)
		{
			for (int l = 0; l < obj.Length; l++) this.writer.Write(obj[l]);
		}
		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(ulong[] obj)
		{
			for (int l = 0; l < obj.Length; l++) this.writer.Write(obj[l]);
		}
		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(float[] obj)
		{
			for (int l = 0; l < obj.Length; l++) this.writer.Write(obj[l]);
		}
		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(double[] obj)
		{
			for (int l = 0; l < obj.Length; l++) this.writer.Write(obj[l]);
		}
		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(decimal[] obj)
		{
			for (int l = 0; l < obj.Length; l++) this.writer.Write(obj[l]);
		}
		/// <summary>
		/// Writes a primitive array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(char[] obj)
		{
			for (int l = 0; l < obj.Length; l++) this.writer.Write(obj[l]);
		}
		/// <summary>
		/// Writes a string array.
		/// </summary>
		/// <param name="obj"></param>
		protected void WriteArrayData(string[] obj)
		{
			for (int l = 0; l < obj.Length; l++)
			{
				if (obj[l] == null)
					this.writer.Write(false);
				else
				{
					this.writer.Write(true);
					this.writer.Write(obj[l]);
				}
			}
		}

		/// <summary>
		/// Writes a <see cref="Duality.Serialization.DataType"/>.
		/// </summary>
		/// <param name="dt"></param>
		protected void WriteDataType(DataType dt)
		{
			this.writer.Write((ushort)dt);
		}
		/// <summary>
		/// Reads a <see cref="Duality.Serialization.DataType"/>.
		/// </summary>
		/// <returns></returns>
		protected DataType ReadDataType()
		{
			DataType dataType = (DataType)this.reader.ReadUInt16();
			if (dataType == (DataType)24) dataType = DataType.Struct; // Legacy support (Written 2014-03-10)
			return dataType;
		}
		/// <summary>
		/// Writes the begin of a new "safe zone", usually encapsulating a data set.
		/// If any error occurs within a safe zone, it is guaranteed to not affect any other
		/// safe zone, although the damaged safe zone itsself might be unusable. In general,
		/// a safe zone prevents an error from affecting any of the zones surroundings.
		/// Safe zones may also be nested.
		/// </summary>
		/// <seealso cref="WritePopOffset"/>
		protected void WritePushOffset()
		{
			this.offsetStack.Push(this.writer.BaseStream.Position);
			this.writer.Write(0L);
		}
		/// <summary>
		/// Writes the end of the most recent "safe zone", usually encapsulating a data set.
		/// If any error occurs within a safe zone, it is guaranteed to not affect any other
		/// safe zone, although the damaged safe zone itsself might be unusable. In general,
		/// a safe zone prevents an error from affecting any of the zones surroundings.
		/// Safe zones may also be nested.
		/// </summary>
		/// <seealso cref="WritePushOffset"/>
		protected void WritePopOffset()
		{
			long curPos = this.writer.BaseStream.Position;
			long lastPos = this.offsetStack.Pop();
			long offset = curPos - lastPos;
			
			this.writer.BaseStream.Seek(lastPos, SeekOrigin.Begin);
			this.writer.Write(offset);
			this.writer.BaseStream.Seek(curPos, SeekOrigin.Begin);
		}

		protected override void BeginReadOperation()
		{
			base.BeginReadOperation();
			this.ReadFormatterHeader();
		}
		protected override void BeginWriteOperation()
		{
			base.BeginWriteOperation();
			this.WriteFormatterHeader();
		}
		protected override void EndReadOperation()
		{
			base.EndReadOperation();
			this.typeDataLayout.Clear();
			this.typeDataLayoutMap.Clear();
			this.offsetStack.Clear();
		}
		protected override void EndWriteOperation()
		{
			base.EndWriteOperation();
			this.typeDataLayout.Clear();
			this.typeDataLayoutMap.Clear();
			this.offsetStack.Clear();
			this.writer.Flush();
		}
	}
}
