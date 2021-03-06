// Copyright 2004-2011 Castle Project - http://www.castleproject.org/
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Telerik.JustMock.Core.Castle.DynamicProxy
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Reflection;
	using System.Reflection.Emit;

	using Telerik.JustMock.Core.Castle.Core.Internal;
	using Telerik.JustMock.Core.Castle.DynamicProxy.Generators;
	using Telerik.JustMock.Core.Castle.DynamicProxy.Internal;

	/// <summary>
	///   Summary description for ModuleScope.
	/// </summary>
	internal class ModuleScope
	{
		/// <summary>
		///   The default file name used when the assembly is saved using <see cref = "DEFAULT_FILE_NAME" />.
		/// </summary>
		public static readonly String DEFAULT_FILE_NAME = "Telerik.JustMock.Dynamic.dll";

		/// <summary>
		///   The default assembly (simple) name used for the assemblies generated by a <see cref = "ModuleScope" /> instance.
		/// </summary>
		public static readonly String DEFAULT_ASSEMBLY_NAME = "Telerik.JustMock";

		private ModuleBuilder moduleBuilderWithStrongName;
		private ModuleBuilder moduleBuilder;

		// The names to use for the generated assemblies and the paths (including the names) of their manifest modules
		private readonly string strongAssemblyName;
		private readonly string weakAssemblyName;
		private readonly string strongModulePath;
		private readonly string weakModulePath;

		// Keeps track of generated types
		private readonly Dictionary<CacheKey, Type> typeCache = new Dictionary<CacheKey, Type>();

		// Users of ModuleScope should use this lock when accessing the cache
		private readonly Lock cacheLock = Lock.Create();

		// Used to lock the module builder creation
		private readonly object moduleLocker = new object();

		// Specified whether the generated assemblies are intended to be saved
		private readonly bool savePhysicalAssembly;
		private readonly bool disableSignedModule;
		private readonly INamingScope namingScope;

		private readonly InternalsUtil internalsUtil;

		/// <summary>
		///   Initializes a new instance of the <see cref = "ModuleScope" /> class; assemblies created by this instance will not be saved.
		/// </summary>
		public ModuleScope() : this(false, false)
		{
		}

		/// <summary>
		///   Initializes a new instance of the <see cref = "ModuleScope" /> class, allowing to specify whether the assemblies generated by this instance
		///   should be saved.
		/// </summary>
		/// <param name = "savePhysicalAssembly">If set to <c>true</c> saves the generated module.</param>
		public ModuleScope(bool savePhysicalAssembly)
			: this(savePhysicalAssembly, false)
		{
		}

		/// <summary>
		///   Initializes a new instance of the <see cref = "ModuleScope" /> class, allowing to specify whether the assemblies generated by this instance
		///   should be saved.
		/// </summary>
		/// <param name = "savePhysicalAssembly">If set to <c>true</c> saves the generated module.</param>
		/// <param name = "disableSignedModule">If set to <c>true</c> disables ability to generate signed module. This should be used in cases where ran under constrained permissions.</param>
		public ModuleScope(bool savePhysicalAssembly, bool disableSignedModule)
			: this(
				savePhysicalAssembly, disableSignedModule, DEFAULT_ASSEMBLY_NAME, DEFAULT_FILE_NAME, DEFAULT_ASSEMBLY_NAME,
				DEFAULT_FILE_NAME)
		{
		}

		/// <summary>
		///   Initializes a new instance of the <see cref = "ModuleScope" /> class, allowing to specify whether the assemblies generated by this instance
		///   should be saved and what simple names are to be assigned to them.
		/// </summary>
		/// <param name = "savePhysicalAssembly">If set to <c>true</c> saves the generated module.</param>
		/// <param name = "disableSignedModule">If set to <c>true</c> disables ability to generate signed module. This should be used in cases where ran under constrained permissions.</param>
		/// <param name = "strongAssemblyName">The simple name of the strong-named assembly generated by this <see
		///    cref = "ModuleScope" />.</param>
		/// <param name = "strongModulePath">The path and file name of the manifest module of the strong-named assembly generated by this <see
		///    cref = "ModuleScope" />.</param>
		/// <param name = "weakAssemblyName">The simple name of the weak-named assembly generated by this <see cref = "ModuleScope" />.</param>
		/// <param name = "weakModulePath">The path and file name of the manifest module of the weak-named assembly generated by this <see
		///    cref = "ModuleScope" />.</param>
		public ModuleScope(bool savePhysicalAssembly, bool disableSignedModule, string strongAssemblyName,
						   string strongModulePath,
						   string weakAssemblyName, string weakModulePath)
			: this(
				savePhysicalAssembly, disableSignedModule, new NamingScope(), strongAssemblyName, strongModulePath, weakAssemblyName,
				weakModulePath)
		{
		}

		/// <summary>
		///   Initializes a new instance of the <see cref = "ModuleScope" /> class, allowing to specify whether the assemblies generated by this instance
		///   should be saved and what simple names are to be assigned to them.
		/// </summary>
		/// <param name = "savePhysicalAssembly">If set to <c>true</c> saves the generated module.</param>
		/// <param name = "disableSignedModule">If set to <c>true</c> disables ability to generate signed module. This should be used in cases where ran under constrained permissions.</param>
		/// <param name = "namingScope">Naming scope used to provide unique names to generated types and their members (usually via sub-scopes).</param>
		/// <param name = "strongAssemblyName">The simple name of the strong-named assembly generated by this <see
		///    cref = "ModuleScope" />.</param>
		/// <param name = "strongModulePath">The path and file name of the manifest module of the strong-named assembly generated by this <see
		///    cref = "ModuleScope" />.</param>
		/// <param name = "weakAssemblyName">The simple name of the weak-named assembly generated by this <see cref = "ModuleScope" />.</param>
		/// <param name = "weakModulePath">The path and file name of the manifest module of the weak-named assembly generated by this <see
		///    cref = "ModuleScope" />.</param>
		public ModuleScope(bool savePhysicalAssembly, bool disableSignedModule, INamingScope namingScope,
						   string strongAssemblyName, string strongModulePath,
						   string weakAssemblyName, string weakModulePath)
		{
			this.savePhysicalAssembly = savePhysicalAssembly;
			this.disableSignedModule = disableSignedModule;
			this.namingScope = namingScope;
			this.strongAssemblyName = strongAssemblyName;
			this.strongModulePath = strongModulePath;
			this.weakAssemblyName = weakAssemblyName;
			this.weakModulePath = weakModulePath;

			this.internalsUtil = new InternalsUtil(this);
		}

		public INamingScope NamingScope
		{
			get { return namingScope; }
		}

		public InternalsUtil Internals
		{
			get { return this.internalsUtil; }
		}

		/// <summary>
		///   Users of this <see cref = "ModuleScope" /> should use this lock when accessing the cache.
		/// </summary>
		public Lock Lock
		{
			get { return cacheLock; }
		}

		/// <summary>
		///   Returns a type from this scope's type cache, or null if the key cannot be found.
		/// </summary>
		/// <param name = "key">The key to be looked up in the cache.</param>
		/// <returns>The type from this scope's type cache matching the key, or null if the key cannot be found</returns>
		public Type GetFromCache(CacheKey key)
		{
			Type type;
			typeCache.TryGetValue(key, out type);
			return type;
		}

		/// <summary>
		///   Registers a type in this scope's type cache.
		/// </summary>
		/// <param name = "key">The key to be associated with the type.</param>
		/// <param name = "type">The type to be stored in the cache.</param>
		public void RegisterInCache(CacheKey key, Type type)
		{
			typeCache[key] = type;
		}

		/// <summary>
		///   Gets the key pair used to sign the strong-named assembly generated by this <see cref = "ModuleScope" />.
		/// </summary>
		/// <returns></returns>
		public static byte[] GetKeyPair()
		{
			return keyPairStrongKeyNameBytes;
		}

		/// <summary>
		///   Gets the strong-named module generated by this scope, or <see langword = "null" /> if none has yet been generated.
		/// </summary>
		/// <value>The strong-named module generated by this scope, or <see langword = "null" /> if none has yet been generated.</value>
		public ModuleBuilder StrongNamedModule
		{
			get { return moduleBuilderWithStrongName; }
		}

		/// <summary>
		///   Gets the file name of the strongly named module generated by this scope.
		/// </summary>
		/// <value>The file name of the strongly named module generated by this scope.</value>
		public string StrongNamedModuleName
		{
			get { return Path.GetFileName(strongModulePath); }
		}

#if !SILVERLIGHT
		/// <summary>
		///   Gets the directory where the strongly named module generated by this scope will be saved, or <see langword = "null" /> if the current directory
		///   is used.
		/// </summary>
		/// <value>The directory where the strongly named module generated by this scope will be saved when <see
		///    cref = "SaveAssembly()" /> is called
		///   (if this scope was created to save modules).</value>
		public string StrongNamedModuleDirectory
		{
			get
			{
				var directory = Path.GetDirectoryName(strongModulePath);
				if (string.IsNullOrEmpty(directory))
				{
					return null;
				}
				return directory;
			}
		}
#endif

		/// <summary>
		///   Gets the weak-named module generated by this scope, or <see langword = "null" /> if none has yet been generated.
		/// </summary>
		/// <value>The weak-named module generated by this scope, or <see langword = "null" /> if none has yet been generated.</value>
		public ModuleBuilder WeakNamedModule
		{
			get { return moduleBuilder; }
		}

		/// <summary>
		///   Gets the file name of the weakly named module generated by this scope.
		/// </summary>
		/// <value>The file name of the weakly named module generated by this scope.</value>
		public string WeakNamedModuleName
		{
			get { return Path.GetFileName(weakModulePath); }
		}

		public string WeakAssemblyName { get { return this.weakAssemblyName; } }
		public string StrongAssemblyName { get { return this.strongAssemblyName; } }

#if !SILVERLIGHT
		/// <summary>
		///   Gets the directory where the weakly named module generated by this scope will be saved, or <see langword = "null" /> if the current directory
		///   is used.
		/// </summary>
		/// <value>The directory where the weakly named module generated by this scope will be saved when <see
		///    cref = "SaveAssembly()" /> is called
		///   (if this scope was created to save modules).</value>
		public string WeakNamedModuleDirectory
		{
			get
			{
				var directory = Path.GetDirectoryName(weakModulePath);
				if (directory == string.Empty)
				{
					return null;
				}
				return directory;
			}
		}
#endif

		/// <summary>
		///   Gets the specified module generated by this scope, creating a new one if none has yet been generated.
		/// </summary>
		/// <param name = "isStrongNamed">If set to true, a strong-named module is returned; otherwise, a weak-named module is returned.</param>
		/// <returns>A strong-named or weak-named module generated by this scope, as specified by the <paramref
		///    name = "isStrongNamed" /> parameter.</returns>
		public ModuleBuilder ObtainDynamicModule(bool isStrongNamed)
		{
			return ObtainDynamicModuleWithStrongName();
		}

		/// <summary>
		///   Gets the strong-named module generated by this scope, creating a new one if none has yet been generated.
		/// </summary>
		/// <returns>A strong-named module generated by this scope.</returns>
		public ModuleBuilder ObtainDynamicModuleWithStrongName()
		{
			if (disableSignedModule)
			{
				throw new InvalidOperationException(
					"Usage of signed module has been disabled. Use unsigned module or enable signed module.");
			}
			lock (moduleLocker)
			{
				if (moduleBuilderWithStrongName == null)
				{
					moduleBuilderWithStrongName = CreateModule(true);
				}
				return moduleBuilderWithStrongName;
			}
		}

		/// <summary>
		///   Gets the weak-named module generated by this scope, creating a new one if none has yet been generated.
		/// </summary>
		/// <returns>A weak-named module generated by this scope.</returns>
		public ModuleBuilder ObtainDynamicModuleWithWeakName()
		{
			lock (moduleLocker)
			{
				if (moduleBuilder == null)
				{
					moduleBuilder = CreateModule(false);
				}
				return moduleBuilder;
			}
		}

		private ModuleBuilder CreateModule(bool signStrongName)
		{
			var assemblyName = GetAssemblyName(signStrongName);
			var moduleName = signStrongName ? StrongNamedModuleName : WeakNamedModuleName;
#if !SILVERLIGHT
			if (savePhysicalAssembly)
			{
				AssemblyBuilder assemblyBuilder;
				try
				{
					assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(
						assemblyName, AssemblyBuilderAccess.RunAndSave, signStrongName ? StrongNamedModuleDirectory : WeakNamedModuleDirectory);
				}
				catch (ArgumentException e)
				{
					if (signStrongName == false && e.StackTrace.Contains("ComputePublicKey") == false)
					{
						// I have no idea what that could be
						throw;
					}
					var message =
						string.Format(
							"There was an error creating dynamic assembly for your proxies - you don't have permissions required to sign the assembly. To workaround it you can enforce generating non-signed assembly only when creating {0}. ALternatively ensure that your account has all the required permissions.",
							GetType());
					throw new ArgumentException(message, e);
				}
				var module = assemblyBuilder.DefineDynamicModule(moduleName, moduleName, false);
				return module;
			}
			else
#endif
			{
				AssemblyBuilder assemblyBuilder;
				try
				{
					assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(
						assemblyName,
						AssemblyBuilderAccess.Run);
				}
				catch (ArgumentException ex)
				{
					GC.KeepAlive(ex);
#if SILVERLIGHT
					throw new MockException("Silverlight appears to be running under Internet Explorer's Protected Mode. Disable Protected Mode, run your application in a less restrictive security zone or run it as an out-of-browser (OOB) application. More information: http://msdn.microsoft.com/en-us/library/bb250462.aspx", ex);
#else
					throw;
#endif
				}

				var module = assemblyBuilder.DefineDynamicModule(moduleName, false);
				return module;
			}
		}

		private AssemblyName GetAssemblyName(bool signStrongName)
		{
			var keyPairStream = signStrongName ? GetKeyPair() : null;
			var name = signStrongName ? strongAssemblyName : weakAssemblyName;
			return MockingUtil.GetStrongAssemblyName(name, keyPairStream);
		}

#if !SILVERLIGHT
		/// <summary>
		///   Saves the generated assembly with the name and directory information given when this <see cref = "ModuleScope" /> instance was created (or with
		///   the <see cref = "DEFAULT_FILE_NAME" /> and current directory if none was given).
		/// </summary>
		/// <remarks>
		///   <para>
		///     This method stores the generated assembly in the directory passed as part of the module information specified when this instance was
		///     constructed (if any, else the current directory is used). If both a strong-named and a weak-named assembly
		///     have been generated, it will throw an exception; in this case, use the <see cref = "SaveAssembly (bool)" /> overload.
		///   </para>
		///   <para>
		///     If this <see cref = "ModuleScope" /> was created without indicating that the assembly should be saved, this method does nothing.
		///   </para>
		/// </remarks>
		/// <exception cref = "InvalidOperationException">Both a strong-named and a weak-named assembly have been generated.</exception>
		/// <returns>The path of the generated assembly file, or null if no file has been generated.</returns>
		public string SaveAssembly()
		{
			if (!savePhysicalAssembly)
			{
				return null;
			}

			if (StrongNamedModule != null && WeakNamedModule != null)
			{
				throw new InvalidOperationException("Both a strong-named and a weak-named assembly have been generated.");
			}

			if (StrongNamedModule != null)
			{
				return SaveAssembly(true);
			}

			if (WeakNamedModule != null)
			{
				return SaveAssembly(false);
			}

			return null;
		}

		/// <summary>
		///   Saves the specified generated assembly with the name and directory information given when this <see
		///    cref = "ModuleScope" /> instance was created
		///   (or with the <see cref = "DEFAULT_FILE_NAME" /> and current directory if none was given).
		/// </summary>
		/// <param name = "strongNamed">True if the generated assembly with a strong name should be saved (see <see
		///    cref = "StrongNamedModule" />);
		///   false if the generated assembly without a strong name should be saved (see <see cref = "WeakNamedModule" />.</param>
		/// <remarks>
		///   <para>
		///     This method stores the specified generated assembly in the directory passed as part of the module information specified when this instance was
		///     constructed (if any, else the current directory is used).
		///   </para>
		///   <para>
		///     If this <see cref = "ModuleScope" /> was created without indicating that the assembly should be saved, this method does nothing.
		///   </para>
		/// </remarks>
		/// <exception cref = "InvalidOperationException">No assembly has been generated that matches the <paramref
		///    name = "strongNamed" /> parameter.
		/// </exception>
		/// <returns>The path of the generated assembly file, or null if no file has been generated.</returns>
		public string SaveAssembly(bool strongNamed)
		{
			if (!savePhysicalAssembly)
			{
				return null;
			}

			AssemblyBuilder assemblyBuilder;
			string assemblyFileName;
			string assemblyFilePath;

			if (strongNamed)
			{
				if (StrongNamedModule == null)
				{
					throw new InvalidOperationException("No strong-named assembly has been generated.");
				}
				assemblyBuilder = (AssemblyBuilder)StrongNamedModule.Assembly;
				assemblyFileName = StrongNamedModuleName;
				assemblyFilePath = StrongNamedModule.FullyQualifiedName;
			}
			else
			{
				if (WeakNamedModule == null)
				{
					throw new InvalidOperationException("No weak-named assembly has been generated.");
				}
				assemblyBuilder = (AssemblyBuilder)WeakNamedModule.Assembly;
				assemblyFileName = WeakNamedModuleName;
				assemblyFilePath = WeakNamedModule.FullyQualifiedName;
			}

			if (File.Exists(assemblyFilePath))
			{
				File.Delete(assemblyFilePath);
			}

			AddCacheMappings(assemblyBuilder);
			assemblyBuilder.Save(assemblyFileName);
			return assemblyFilePath;
		}
#endif

#if !SILVERLIGHT
		private void AddCacheMappings(AssemblyBuilder builder)
		{
			Dictionary<CacheKey, string> mappings;

			using (Lock.ForReading())
			{
				mappings = new Dictionary<CacheKey, string>();
				foreach (var cacheEntry in typeCache)
				{
					// NOTE: using == returns invalid results.
					// we need to use Equals here for it to work properly
					if(builder.Equals(cacheEntry.Value.Assembly))
					{
						mappings.Add(cacheEntry.Key, cacheEntry.Value.FullName);
					}
				}
			}

			CacheMappingsAttribute.ApplyTo(builder, mappings);
		}
#endif

#if !SILVERLIGHT
		/// <summary>
		///   Loads the generated types from the given assembly into this <see cref = "ModuleScope" />'s cache.
		/// </summary>
		/// <param name = "assembly">The assembly to load types from. This assembly must have been saved via <see
		///    cref = "SaveAssembly(bool)" /> or
		///   <see cref = "SaveAssembly()" />, or it must have the <see cref = "CacheMappingsAttribute" /> manually applied.</param>
		/// <remarks>
		///   This method can be used to load previously generated and persisted proxy types from disk into this scope's type cache, eg. in order
		///   to avoid the performance hit associated with proxy generation.
		/// </remarks>
		public void LoadAssemblyIntoCache(Assembly assembly)
		{
			if (assembly == null)
			{
				throw new ArgumentNullException("assembly");
			}

			var cacheMappings =
				(CacheMappingsAttribute[])assembly.GetCustomAttributes(typeof(CacheMappingsAttribute), false);

			if (cacheMappings.Length == 0)
			{
				var message = string.Format(
					"The given assembly '{0}' does not contain any cache information for generated types.",
					assembly.FullName);
				throw new ArgumentException(message, "assembly");
			}

			foreach (var mapping in cacheMappings[0].GetDeserializedMappings())
			{
				var loadedType = assembly.GetType(mapping.Value);

				if (loadedType != null)
				{
					RegisterInCache(mapping.Key, loadedType);
				}
			}
		}
#endif

		public TypeBuilder DefineType(bool inSignedModulePreferably, string name, TypeAttributes flags)
		{
			var module = ObtainDynamicModule(disableSignedModule == false && inSignedModulePreferably);
			return module.DefineType(name, flags);
		}

		private static readonly byte[] keyPairStrongKeyNameBytes =
			{
				0x07, 0x02, 0x00, 0x00, 0x00, 0x24, 0x00, 0x00, 0x52, 0x53, 0x41, 0x32, 0x00, 0x04, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x09,
				0x8B, 0x14, 0x34, 0xE5, 0x98, 0xC6, 0x56, 0xB2, 0x2E, 0xB5, 0x90, 0x00, 0xB0, 0xBF, 0x73, 0x31, 0x0C, 0xB8, 0x48, 0x8A, 0x6B,
				0x63, 0xDB, 0x1D, 0x35, 0x45, 0x7F, 0x2F, 0x93, 0x9F, 0x92, 0x74, 0x14, 0x92, 0x1A, 0x76, 0x98, 0x21, 0xF3, 0x71, 0xC3, 0x1A,
				0x8C, 0x1D, 0x4B, 0x73, 0xF8, 0xE9, 0x34, 0xE2, 0xA0, 0x76, 0x9D, 0xE4, 0xD8, 0x74, 0xE0, 0xA5, 0x17, 0xD3, 0xD7, 0xB9, 0xC3,
				0x6C, 0xD0, 0xFF, 0xCE, 0xA2, 0x14, 0x2F, 0x60, 0x97, 0x4C, 0x6E, 0xB0, 0x08, 0x01, 0xDE, 0x45, 0x43, 0xEF, 0x7E, 0x93, 0xF7,
				0x96, 0x87, 0xB0, 0x40, 0xD9, 0x67, 0xBB, 0x6B, 0xD5, 0x5C, 0xA0, 0x93, 0x71, 0x1B, 0x01, 0x39, 0x67, 0xA0, 0x96, 0xD5, 0x24,
				0xA9, 0xCA, 0xDF, 0x94, 0xE3, 0xB7, 0x48, 0xEB, 0xDA, 0xE7, 0x94, 0x7E, 0xA6, 0xDE, 0x66, 0x22, 0xEA, 0xBF, 0x65, 0x48, 0x44,
				0x8E, 0x19, 0xF4, 0xC0, 0x26, 0x67, 0x10, 0xA3, 0xC5, 0x40, 0x5B, 0xE2, 0xC8, 0x11, 0x5D, 0x33, 0x61, 0x6F, 0x36, 0xA5, 0xB3,
				0xAF, 0xF2, 0xB7, 0x44, 0x29, 0x49, 0x07, 0xAA, 0x24, 0x52, 0xF3, 0xE9, 0x54, 0x25, 0x69, 0x30, 0x0F, 0xE2, 0xB6, 0x53, 0xB0,
				0xFF, 0x81, 0x49, 0x4D, 0x17, 0xC1, 0xB8, 0xFB, 0xEB, 0x9A, 0x54, 0x9B, 0x2A, 0x7B, 0xD0, 0x1F, 0xCA, 0xEE, 0x6A, 0x7F, 0x9A,
				0x0D, 0xC3, 0x71, 0xAC, 0x28, 0xF9, 0xE9, 0x3F, 0x5F, 0xA7, 0xA0, 0x8D, 0x34, 0x86, 0x8A, 0xB7, 0xEE, 0xF7, 0x64, 0xD1, 0x90,
				0xB2, 0xC4, 0x3A, 0xE2, 0x69, 0xF6, 0x52, 0x3C, 0xCE, 0x8B, 0xC9, 0xD5, 0xD4, 0x00, 0x02, 0x25, 0xED, 0xDA, 0x67, 0x53, 0xE5,
				0xF5, 0xDD, 0x1D, 0x10, 0x63, 0x8E, 0x0A, 0xFA, 0x48, 0x33, 0xA5, 0x59, 0x97, 0xE3, 0xBD, 0xE5, 0xBB, 0x25, 0x5F, 0x0B, 0xBB,
				0x44, 0xB8, 0xBA, 0x39, 0x0D, 0x40, 0x31, 0x0C, 0x76, 0xAE, 0x08, 0x03, 0x5F, 0xFA, 0x04, 0x6B, 0x31, 0xBC, 0x25, 0x94, 0xA9,
				0x4C, 0xFA, 0x88, 0x26, 0x71, 0x5E, 0x88, 0x79, 0xD6, 0x5B, 0xAC, 0xD0, 0xED, 0xB7, 0xC7, 0x7F, 0x94, 0xD5, 0x83, 0x0F, 0xF4,
				0x30, 0x4A, 0x9D, 0x4D, 0xAC, 0xA1, 0x9F, 0xCF, 0x09, 0x06, 0xD4, 0x4B, 0xB4, 0xE7, 0xB4, 0x83, 0x3F, 0xCD, 0xE2, 0xD9, 0x12,
				0x8B, 0x41, 0x5F, 0x05, 0xF1, 0xD0, 0xA1, 0xEE, 0xBE, 0x7F, 0xF5, 0x29, 0x77, 0x26, 0x41, 0x50, 0x72, 0xF4, 0x30, 0x11, 0x63,
				0xD0, 0x8F, 0xE5, 0x6C, 0xB5, 0x44, 0xA9, 0xDA, 0x3F, 0xDA, 0x2D, 0x52, 0x93, 0x3F, 0xDE, 0x43, 0xB6, 0xC0, 0x90, 0x5C, 0x48,
				0x2B, 0xA4, 0xBC, 0x92, 0x73, 0xCB, 0xA5, 0xF6, 0xCC, 0x42, 0x27, 0x91, 0xFE, 0xA3, 0xD3, 0x37, 0x49, 0x03, 0x86, 0xF2, 0x30,
				0xE7, 0xE0, 0x0E, 0x92, 0x8E, 0x80, 0xEC, 0x51, 0xF2, 0x77, 0xC8, 0x9B, 0x66, 0xCD, 0xED, 0xA1, 0x3C, 0xE3, 0x40, 0x6E, 0xAF,
				0x1E, 0x86, 0x8C, 0x19, 0xB1, 0x1C, 0x06, 0x31, 0xA5, 0x82, 0x59, 0x71, 0x7D, 0x40, 0xE4, 0x08, 0x00, 0x34, 0x38, 0x7E, 0x34,
				0xDC, 0xDB, 0xE6, 0x7A, 0x07, 0xF6, 0x5E, 0x38, 0xC6, 0x84, 0xDC, 0x66, 0x49, 0x25, 0x33, 0x88, 0x9D, 0x56, 0x07, 0x3F, 0x0F,
				0x15, 0xBB, 0x7D, 0xB2, 0xBA, 0x5A, 0x01, 0x4B, 0x82, 0x49, 0x30, 0x8E, 0x55, 0x2C, 0xCE, 0xC3, 0x57, 0x65, 0x75, 0xF4, 0xB7,
				0x3C, 0xBA, 0xEB, 0x2F, 0x05, 0xF5, 0x32, 0x87, 0xA7, 0x31, 0xF7, 0x50, 0xFC, 0xF2, 0xB0, 0xD0, 0x00, 0xFA, 0xA0, 0x26, 0xBB,
				0x68, 0x34, 0xE8, 0x29, 0x8B, 0x72, 0xA5, 0x45, 0x6F, 0x87, 0x0D, 0xF8, 0xA9, 0x09, 0x96, 0xFC, 0x5F, 0x0F, 0xA9, 0x09, 0xA6,
				0x77, 0x8E, 0xA7, 0x16, 0x64, 0x2F, 0x1B, 0xB8, 0x98, 0xE0, 0x63, 0x14, 0x70, 0x86, 0xD2, 0xA7, 0x94, 0x31, 0xC0, 0x6F, 0x52,
				0x24, 0xE8, 0x01, 0xDC, 0x97, 0x86, 0x2C, 0x6C, 0x32, 0xCA, 0x12, 0x9E, 0xA7, 0x4E, 0x94, 0xF7, 0x78, 0xC2, 0x30, 0xA8, 0xA1,
				0x28, 0x0B, 0x99, 0xD9, 0xD2, 0x24, 0xCC, 0xBD, 0x16, 0x94, 0xAD, 0x6D, 0x8B, 0x90, 0x88, 0x30, 0x81, 0xEA, 0x10, 0xCB, 0x94,
				0xEF, 0x84, 0xC7, 0xCA, 0x8B, 0xA1, 0xD5, 0x6F
			};
	}
}
