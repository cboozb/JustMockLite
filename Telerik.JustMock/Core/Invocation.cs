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
using System.Reflection;
using System.Text;

namespace Telerik.JustMock.Core
{
	public sealed class Invocation
	{
		private MethodBase method;

		private object returnValue;
		private bool isReturnValueSet;

		internal object Instance { get; private set; }

		internal object[] Args { get; private set; }

		internal object ReturnValue
		{
			get { return this.returnValue; }
			set
			{
				this.returnValue = value;
				isReturnValueSet = true;
			}
		}

		internal bool IsReturnValueSet
		{
			get { return this.isReturnValueSet; }
		}

		internal MethodBase MethodInvocationTarget { get; set; }
		internal bool CallOriginal { get; set; }
		internal bool UserProvidedImplementation { get; set; }
		internal bool InArrange { get; set; }
		internal bool Recording { get; set; }
		internal bool RetainBehaviorDuringRecording { get; set; }
		internal MocksRepository Repository { get; set; }

		internal Action ExceptionThrower { get; set; }

		internal Invocation(object instance, MethodBase method, object[] args)
		{
			this.Instance = instance;
			this.Method = method;
			this.MethodInvocationTarget = method;
			this.Args = args;
		}

		internal void ThrowExceptionIfNecessary()
		{
			if (ExceptionThrower != null)
				ExceptionThrower();
		}

		internal MethodBase Method
		{
			get
			{
				return this.method;
			}
			private set
			{
				if (value != null)
				{
					if (value.ContainsGenericParameters)
						throw new ArgumentException("Invocation method must be a concrete method");
				}

				var asMethodInfo = value as MethodInfo;
				if (asMethodInfo != null)
					value = asMethodInfo.NormalizeComInterfaceMethod();

				this.method = value;
			}
		}

		internal string InputToString()
		{
			var sb = new StringBuilder();
			sb.AppendFormat("{0}.{1}(", Instance != null ? MockingUtil.GetUnproxiedType(Instance) : method.DeclaringType, method.Name);
			sb.Append(", ".Join(Args));
			sb.Append(")");
			return sb.ToString();
		}
	}
}
