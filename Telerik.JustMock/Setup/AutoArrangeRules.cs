using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Telerik.JustMock.Core;
using Telerik.JustMock.Core.Behaviors;

namespace Telerik.JustMock.Setup
{
	public static class LooseBehaviorReturnRules
	{
		public static readonly List<ILooseBehaviorReturnRule> Rules = new List<ILooseBehaviorReturnRule>
		{
			new ArrayLooseBehaviorReturnRule(),
			new DictionaryLooseBehaviorReturnRule(),
			new EnumerableLooseBehaviorReturnRule(),
		};

		internal static object CreateValue(Type type, IMockMixin arrangedMock)
		{
			foreach (var rule in Rules)
			{
				var value = rule.CreateValue(type, arrangedMock);
				if (value != null)
				{
					return value;
				}
			}
			return null;
		}
	}

	public interface ILooseBehaviorReturnRule
	{
		object CreateValue(Type type, IMockMixin arrangedMock);
	}

	internal class ArrayLooseBehaviorReturnRule : ILooseBehaviorReturnRule
	{
		public object CreateValue(Type type, IMockMixin arrangedMock)
		{
			return type.IsArray
				? Array.CreateInstance(type.GetElementType(), Enumerable.Repeat(0, type.GetArrayRank()).ToArray())
				: null;
		}
	}

	internal class DictionaryLooseBehaviorReturnRule : ILooseBehaviorReturnRule
	{
		public object CreateValue(Type type, IMockMixin arrangedMock)
		{
			var idictionaryType = type.GetImplementationOfGenericInterface(typeof(IDictionary<,>));
			if (idictionaryType != null)
			{
				var dictType = typeof(Dictionary<,>).MakeGenericType(idictionaryType.GetGenericArguments());
				return MockCollection.Create(type, arrangedMock.Repository, arrangedMock as IMockReplicator, (IEnumerable)MockingUtil.CreateInstance(dictType));
			}
			return null;
		}
	}

	internal class EnumerableLooseBehaviorReturnRule : ILooseBehaviorReturnRule
	{
		public object CreateValue(Type type, IMockMixin arrangedMock)
		{
			var ienumerableType = type.GetImplementationOfGenericInterface(typeof(IEnumerable<>));
			if (ienumerableType != null)
			{
				var listType = typeof(List<>).MakeGenericType(ienumerableType.GetGenericArguments());
				return MockCollection.Create(type, arrangedMock.Repository, arrangedMock as IMockReplicator, (IEnumerable)MockingUtil.CreateInstance(listType));
			}
			return null;
		}
	}
}
