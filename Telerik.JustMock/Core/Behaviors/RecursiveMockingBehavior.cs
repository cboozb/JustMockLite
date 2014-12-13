/*
 JustMock Lite
 Copyright © 2010-2014 Telerik AD

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

   http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Telerik.JustMock.Core.Context;
using Telerik.JustMock.Setup;

namespace Telerik.JustMock.Core.Behaviors
{
	internal enum RecursiveMockingBehaviorType
	{
		OnlyDuringAnalysis,
		ReturnDefault,
		ReturnMock,
	}

	internal class RecursiveMockingBehavior : IBehavior
	{
		// can't put the key part in a Dictionary,
		// because we can't be sure that GetHashCode() works
		private readonly Dictionary<MethodBase, List<KeyValuePair<WeakReference, object>>> mocks
			= new Dictionary<MethodBase, List<KeyValuePair<WeakReference, object>>>();

		private readonly RecursiveMockingBehaviorType type;

		public RecursiveMockingBehavior(RecursiveMockingBehaviorType type)
		{
			this.type = type;
		}

		public void Process(Invocation invocation)
		{
			if (invocation.IsReturnValueSet)
				return;

			var returnType = invocation.Method.GetReturnType();
			if (returnType == typeof(void) || returnType.IsValueType)
				return;

			object mock = null;
			List<KeyValuePair<WeakReference, object>> mocksList;
			if (mocks.TryGetValue(invocation.Method, out mocksList))
			{
				for (int i = 0; i < mocksList.Count; )
				{
					var parentMock = mocksList[i].Key.Target;
					if (parentMock == null)
					{
						mocksList.RemoveAt(i);
					}
					else if (Equals(parentMock, invocation.Instance))
					{
						mock = mocksList[i].Value;
						break;
					}
					else
					{
						i++;
					}
				}
			}

			if (mock == null)
			{
				var parentMock = MocksRepository.GetMockMixinFromInvocation(invocation);
				var repository = parentMock.Repository;
				var replicator = parentMock as IMockReplicator;

				bool mustReturnAMock = invocation.InArrange || this.type == RecursiveMockingBehaviorType.ReturnMock;
				if (mustReturnAMock || this.type == RecursiveMockingBehaviorType.ReturnDefault)
				{
					mock = LooseBehaviorReturnRules.CreateValue(returnType, parentMock);

					if (mock == null && mustReturnAMock)
					{
						var stackTrace = new StackTrace();
						var methodCallingArrange = stackTrace.EnumerateFrames()
							.SkipWhile(m => !Attribute.IsDefined(m, typeof(ArrangeMethodAttribute)))
							.SkipWhile(m => m.Module.Assembly == typeof(MocksRepository).Assembly)
							.FirstOrDefault();

						if (methodCallingArrange != null && invocation.Method.DeclaringType.IsAssignableFrom(methodCallingArrange.DeclaringType))
							return;

						if (typeof(String) == returnType)
						{
							mock = String.Empty;
						}
						else
						{
							try
							{
								mock = replicator.CreateSimilarMock(repository, returnType, null, true, null);
							}
							catch (MockException)
							{ }
						}
					}
				}

				if (mock == null)
					return;

				if (mocksList == null)
				{
					mocksList = new List<KeyValuePair<WeakReference, object>>();
					mocks.Add(invocation.Method, mocksList);
				}
				mocksList.Add(new KeyValuePair<WeakReference, object>(new WeakReference(invocation.Instance), mock));

				var mockMixin = MocksRepository.GetMockMixin(mock, null);
				if (parentMock != null && mockMixin != null)
					parentMock.DependentMocks.Add(mock);
			}

			invocation.ReturnValue = mock;
			invocation.CallOriginal = false;
			invocation.UserProvidedImplementation = true;
		}
	}
}
