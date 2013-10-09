﻿
/* Copyright 2011-2013 Roman Kuzmin
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
namespace Mdbc
{
	static class Actor
	{
		internal const string ServerVariable = "Server";
		internal const string DatabaseVariable = "Database";
		internal const string CollectionVariable = "Collection";
		public static object ToObject(BsonValue value) //_120509_173140 keep consistent
		{
			if (value == null)
				return null;

			switch (value.BsonType)
			{
				case BsonType.Array: return new Collection((BsonArray)value); // wrapper
				case BsonType.Binary: return BsonTypeMapper.MapToDotNetValue(value) ?? value; // byte[] or Guid else self
				case BsonType.Boolean: return ((BsonBoolean)value).Value;
				case BsonType.DateTime: return ((BsonDateTime)value).ToUniversalTime();
				case BsonType.Document: return new Dictionary((BsonDocument)value); // wrapper
				case BsonType.Double: return ((BsonDouble)value).Value;
				case BsonType.Int32: return ((BsonInt32)value).Value;
				case BsonType.Int64: return ((BsonInt64)value).Value;
				case BsonType.Null: return null;
				case BsonType.ObjectId: return ((BsonObjectId)value).Value;
				case BsonType.String: return ((BsonString)value).Value;
				default: return value;
			}
		}
		// Checks all types, designed for nested calls without selectors
		public static BsonValue ToBsonValue(object value, Func<object, object> convert)
		{
			return ToBsonValue(value, convert, null);
		}
		static BsonValue ToBsonValue(object value, Func<object, object> convert, HashSet<object> cycle)
		{
			if (value == null)
				return BsonNull.Value;

			var ps = value as PSObject;
			if (ps != null)
			{
				value = ps.BaseObject;

				// case: custom
				if (value is PSCustomObject) //! PSObject keeps properties
					return ToBsonDocumentFromProperties(ps, null, convert, cycle);
			}

			// case: BsonValue
			var bson = value as BsonValue;
			if (bson != null)
				return bson;

			// case: string
			var text = value as string;
			if (text != null)
				return new BsonString(text);

			// case: dictionary
			var dictionary = value as IDictionary;
			if (dictionary != null)
				return ToBsonDocumentFromDictionary(dictionary, null, convert, cycle);

			// case: collection
			var enumerable = value as IEnumerable;
			if (enumerable != null)
			{
				CheckCycle(enumerable, ref cycle);
				
				var array = new BsonArray();
				foreach (var it in enumerable)
					array.Add(ToBsonValue(it, convert, cycle));
				return array;
			}

			// try to create BsonValue
			try
			{
				return BsonValue.Create(value);
			}
			catch (ArgumentException ae)
			{
				if (convert == null)
					throw;

				try
				{
					value = convert(value);
				}
				catch (RuntimeException re)
				{
					throw new ArgumentException( //! use this type
						string.Format(@"Converter script was called on ""{0}"" and failed with ""{1}"".", ae.Message, re.Message), re);
				}

				if (value == null)
					throw;

				// do not call converter twice
				return ToBsonValue(value, null, cycle);
			}
		}
		static BsonDocument ToBsonDocumentFromDictionary(IDictionary dictionary, IEnumerable<Selector> properties, Func<object, object> convert, HashSet<object> cycle)
		{
			CheckCycle(dictionary, ref cycle);
			
			// Mdbc.Dictionary
			var md = dictionary as Mdbc.Dictionary;
			if (md != null)
				return md.Document();

			var document = new BsonDocument();
			if (properties == null)
			{
				foreach (DictionaryEntry de in dictionary)
				{
					var name = de.Key as string;
					if (name == null)
						throw new InvalidOperationException("Dictionary keys must be strings.");

					document.Add(name, ToBsonValue(de.Value, convert, cycle));
				}
			}
			else
			{
				foreach (var selector in properties)
				{
					if (selector.PropertyName != null)
					{
						if (dictionary.Contains(selector.PropertyName))
							document.Add(selector.DocumentName, ToBsonValue(dictionary[selector.PropertyName], convert, cycle));
					}
					else
					{
						document.Add(selector.DocumentName, ToBsonValue(selector.GetValue(dictionary), convert, cycle));
					}
				}
			}

			return document;
		}
		// Input supposed to be not null
		static BsonDocument ToBsonDocumentFromProperties(PSObject value, IEnumerable<Selector> properties, Func<object, object> convert, HashSet<object> cycle)
		{
			CheckCycle(value, ref cycle);
			
			var document = new BsonDocument();
			if (properties == null)
			{
				foreach (var pi in value.Properties)
				{
					try
					{
						document.Add(pi.Name, ToBsonValue(pi.Value, convert, cycle));
					}
					catch (GetValueException) // .Value may throw, e.g. ExitCode in Process
					{
						document.Add(pi.Name, BsonNull.Value);
					}
				}
			}
			else
			{
				foreach (var selector in properties)
				{
					if (selector.PropertyName != null)
					{
						var pi = value.Properties[selector.PropertyName];
						if (pi != null)
						{
							try
							{
								document.Add(selector.DocumentName, ToBsonValue(pi.Value, convert, cycle));
							}
							catch (GetValueException) // .Value may throw, e.g. ExitCode in Process
							{
								document.Add(selector.DocumentName, BsonNull.Value);
							}
						}
					}
					else
					{
						document.Add(selector.DocumentName, ToBsonValue(selector.GetValue(value), convert, cycle));
					}
				}
			}
			return document;
		}
		public static BsonDocument ToBsonDocument(object value, IEnumerable<Selector> properties, Func<object, object> convert)
		{
			return ToBsonDocument(value, properties, convert, null);
		}
		static BsonDocument ToBsonDocument(object value, IEnumerable<Selector> properties, Func<object, object> convert, HashSet<object> cycle)
		{
			var ps = value as PSObject;
			if (ps != null)
				value = ps.BaseObject;

			var dictionary = value as IDictionary;
			if (dictionary != null)
				return ToBsonDocumentFromDictionary(dictionary, properties, convert, cycle);

			var document = value as BsonDocument;
			if (document != null)
				return document;

			return ToBsonDocumentFromProperties(ps ?? PSObject.AsPSObject(value), properties, convert, cycle);
		}
		public static IEnumerable<BsonValue> ToEnumerableBsonValue(object value)
		{
			var bv = ToBsonValue(value, null);
			var ba = bv as BsonArray;
			if (ba == null)
				return new[] { bv };
			else
				return ba;
		}
		public static IMongoQuery DocumentIdToQuery(BsonDocument document)
		{
			return Query.EQ("_id", document["_id"]);
		}
		public static IMongoQuery ObjectToQuery(object value)
		{
			if (value == null)
				return Query.Null;

			var ps = value as PSObject;
			if (ps != null)
			{
				value = ps.BaseObject;

				if (value is PSCustomObject)
				{
					var id = ps.Properties["_id"];
					if (id == null)
						throw new InvalidOperationException("Custom object: expected property _id.");

					return Query.EQ("_id", BsonValue.Create(id.Value));
				}
			}

			var query = value as IMongoQuery;
			if (query != null)
				return query;

			var mdbc = value as Dictionary;
			if (mdbc != null)
				return DocumentIdToQuery(mdbc.Document());

			var bson = value as BsonDocument;
			if (bson != null)
				return DocumentIdToQuery(bson);

			var dictionary = value as IDictionary;
			if (dictionary != null)
				return new QueryDocument(dictionary);

			return Query.EQ("_id", BsonValue.Create(value));
		}
		/// <summary>
		/// Converts PS objects to a SortBy object.
		/// </summary>
		/// <param name="values">Strings or @{Name=Boolean}. Null and empty is allowed.</param>
		/// <returns>SortBy object, may be empty but not null.</returns>
		public static IMongoSortBy ObjectsToSortBy(IEnumerable values)
		{
			if (values == null)
				return SortBy.Null;

			var builder = new SortByBuilder();
			foreach (var it in values)
			{
				var name = it as string;
				if (name != null)
				{
					builder.Ascending(name);
					continue;
				}

				var hash = it as IDictionary;
				if (hash == null) throw new ArgumentException("SortBy: Invalid value type.");
				if (hash.Count != 1) throw new ArgumentException("SortBy: Expected a dictionary with one entry.");

				foreach (DictionaryEntry kv in hash)
				{
					name = kv.Key.ToString();
					if (LanguagePrimitives.IsTrue(kv.Value))
						builder.Ascending(name);
					else
						builder.Descending(name);
				}
			}
			return builder;
		}
		public static IMongoUpdate ObjectToUpdate(object value)
		{
			var ps = value as PSObject;
			if (ps != null)
				value = ps.BaseObject;

			var update = value as IMongoUpdate;
			if (update != null)
				return update;

			var dictionary = value as IDictionary;
			if (dictionary != null)
				return new UpdateDocument(dictionary);

			var enumerable = LanguagePrimitives.GetEnumerable(value);
			if (enumerable != null)
				return Update.Combine(enumerable.Cast<object>().Select(Actor.ObjectToUpdate));

			throw new PSInvalidCastException("Invalid update type. Valid types: update, dictionary.");
		}
		static void CheckCycle(object value, ref HashSet<object> cycle)
		{
			if (cycle == null)
				cycle = new HashSet<object>(new ReferenceEqualityComparer<object>());
			if (!cycle.Add(value))
				throw new InvalidOperationException("Cyclic reference.");
		}
	}
	class ReferenceEqualityComparer<T> : IEqualityComparer<T>
	{
		public bool Equals(T x, T y)
		{
			return object.ReferenceEquals(x, y);
		}
		public int GetHashCode(T obj)
		{
			return obj == null ? 0 : obj.GetHashCode();
		}
	}
}
