// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using RuntimeTypeCache = System.RuntimeType.RuntimeTypeCache;

namespace System.Reflection
{
    internal unsafe sealed class RuntimePropertyInfo : PropertyInfo, ISerializable
    {
        #region Private Data Members
        private int m_token;
        private string m_name;
        private void* m_utf8name;
        private PropertyAttributes m_flags;
        private RuntimeTypeCache m_reflectedTypeCache;
        private RuntimeMethodInfo m_getterMethod;
        private RuntimeMethodInfo m_setterMethod;
        private MethodInfo[] m_otherMethod;
        private RuntimeType m_declaringType;
        private BindingFlags m_bindingFlags;
        private Signature m_signature;
        private ParameterInfo[] m_parameters;
        #endregion

        #region Constructor
        internal RuntimePropertyInfo(
            int tkProperty, RuntimeType declaredType, RuntimeTypeCache reflectedTypeCache, out bool isPrivate)
        {
            Contract.Requires(declaredType != null);
            Contract.Requires(reflectedTypeCache != null);
            Debug.Assert(!reflectedTypeCache.IsGlobal);

            MetadataImport scope = declaredType.GetRuntimeModule().MetadataImport;

            m_token = tkProperty;
            m_reflectedTypeCache = reflectedTypeCache;
            m_declaringType = declaredType;

            ConstArray sig;
            scope.GetPropertyProps(tkProperty, out m_utf8name, out m_flags, out sig);

            RuntimeMethodInfo dummy;
            Associates.AssignAssociates(scope, tkProperty, declaredType, reflectedTypeCache.GetRuntimeType(),
                out dummy, out dummy, out dummy,
                out m_getterMethod, out m_setterMethod, out m_otherMethod,
                out isPrivate, out m_bindingFlags);
        }
        #endregion

        #region Internal Members
        internal override bool CacheEquals(object o)
        {
            RuntimePropertyInfo m = o as RuntimePropertyInfo;

            if ((object)m == null)
                return false;

            return m.m_token == m_token &&
                RuntimeTypeHandle.GetModule(m_declaringType).Equals(
                    RuntimeTypeHandle.GetModule(m.m_declaringType));
        }

        internal Signature Signature
        {
            get
            {
                if (m_signature == null)
                {
                    PropertyAttributes flags;
                    ConstArray sig;

                    void* name;
                    GetRuntimeModule().MetadataImport.GetPropertyProps(
                        m_token, out name, out flags, out sig);

                    m_signature = new Signature(sig.Signature.ToPointer(), (int)sig.Length, m_declaringType);
                }

                return m_signature;
            }
        }
        internal bool EqualsSig(RuntimePropertyInfo target)
        {
            //@Asymmetry - Legacy policy is to remove duplicate properties, including hidden properties. 
            //             The comparison is done by name and by sig. The EqualsSig comparison is expensive 
            //             but forutnetly it is only called when an inherited property is hidden by name or
            //             when an interfaces declare properies with the same signature. 
            //             Note that we intentionally don't resolve generic arguments so that we don't treat
            //             signatures that only match in certain instantiations as duplicates. This has the
            //             down side of treating overriding and overriden properties as different properties
            //             in some cases. But PopulateProperties in rttype.cs should have taken care of that
            //             by comparing VTable slots.
            //
            //             Class C1(Of T, Y)
            //                 Property Prop1(ByVal t1 As T) As Integer
            //                     Get
            //                         ... ...
            //                     End Get
            //                 End Property
            //                 Property Prop1(ByVal y1 As Y) As Integer
            //                     Get
            //                         ... ...
            //                     End Get
            //                 End Property
            //             End Class
            //

            Contract.Requires(Name.Equals(target.Name));
            Contract.Requires(this != target);
            Contract.Requires(this.ReflectedType == target.ReflectedType);

            return Signature.CompareSig(this.Signature, target.Signature);
        }
        internal BindingFlags BindingFlags { get { return m_bindingFlags; } }
        #endregion

        #region Object Overrides
        public override String ToString()
        {
            return FormatNameAndSig(false);
        }

        private string FormatNameAndSig(bool serialization)
        {
            StringBuilder sbName = new StringBuilder(PropertyType.FormatTypeName(serialization));

            sbName.Append(" ");
            sbName.Append(Name);

            RuntimeType[] arguments = Signature.Arguments;
            if (arguments.Length > 0)
            {
                sbName.Append(" [");
                sbName.Append(MethodBase.ConstructParameters(arguments, Signature.CallingConvention, serialization));
                sbName.Append("]");
            }

            return sbName.ToString();
        }
        #endregion

        #region ICustomAttributeProvider
        public override Object[] GetCustomAttributes(bool inherit)
        {
            return CustomAttribute.GetCustomAttributes(this, typeof(object) as RuntimeType);
        }

        public override Object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (attributeType == null)
                throw new ArgumentNullException(nameof(attributeType));
            Contract.EndContractBlock();

            RuntimeType attributeRuntimeType = attributeType.UnderlyingSystemType as RuntimeType;

            if (attributeRuntimeType == null)
                throw new ArgumentException(SR.Arg_MustBeType, nameof(attributeType));

            return CustomAttribute.GetCustomAttributes(this, attributeRuntimeType);
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            if (attributeType == null)
                throw new ArgumentNullException(nameof(attributeType));
            Contract.EndContractBlock();

            RuntimeType attributeRuntimeType = attributeType.UnderlyingSystemType as RuntimeType;

            if (attributeRuntimeType == null)
                throw new ArgumentException(SR.Arg_MustBeType, nameof(attributeType));

            return CustomAttribute.IsDefined(this, attributeRuntimeType);
        }

        public override IList<CustomAttributeData> GetCustomAttributesData()
        {
            return CustomAttributeData.GetCustomAttributesInternal(this);
        }
        #endregion

        #region MemberInfo Overrides
        public override MemberTypes MemberType { get { return MemberTypes.Property; } }
        public override String Name
        {
            get
            {
                if (m_name == null)
                    m_name = new Utf8String(m_utf8name).ToString();

                return m_name;
            }
        }
        public override Type DeclaringType
        {
            get
            {
                return m_declaringType;
            }
        }

        public sealed override bool HasSameMetadataDefinitionAs(MemberInfo other) => HasSameMetadataDefinitionAsCore<RuntimePropertyInfo>(other);

        public override Type ReflectedType
        {
            get
            {
                return ReflectedTypeInternal;
            }
        }

        private RuntimeType ReflectedTypeInternal
        {
            get
            {
                return m_reflectedTypeCache.GetRuntimeType();
            }
        }

        public override int MetadataToken { get { return m_token; } }

        public override Module Module { get { return GetRuntimeModule(); } }
        internal RuntimeModule GetRuntimeModule() { return m_declaringType.GetRuntimeModule(); }
        #endregion

        #region PropertyInfo Overrides

        #region Non Dynamic

        public override Type[] GetRequiredCustomModifiers()
        {
            return Signature.GetCustomModifiers(0, true);
        }

        public override Type[] GetOptionalCustomModifiers()
        {
            return Signature.GetCustomModifiers(0, false);
        }

        internal object GetConstantValue(bool raw)
        {
            Object defaultValue = MdConstant.GetValue(GetRuntimeModule().MetadataImport, m_token, PropertyType.GetTypeHandleInternal(), raw);

            if (defaultValue == DBNull.Value)
                // Arg_EnumLitValueNotFound -> "Literal value was not found."
                throw new InvalidOperationException(SR.Arg_EnumLitValueNotFound);

            return defaultValue;
        }

        public override object GetConstantValue() { return GetConstantValue(false); }

        public override object GetRawConstantValue() { return GetConstantValue(true); }

        public override MethodInfo[] GetAccessors(bool nonPublic)
        {
            List<MethodInfo> accessorList = new List<MethodInfo>();

            if (Associates.IncludeAccessor(m_getterMethod, nonPublic))
                accessorList.Add(m_getterMethod);

            if (Associates.IncludeAccessor(m_setterMethod, nonPublic))
                accessorList.Add(m_setterMethod);

            if ((object)m_otherMethod != null)
            {
                for (int i = 0; i < m_otherMethod.Length; i++)
                {
                    if (Associates.IncludeAccessor(m_otherMethod[i] as MethodInfo, nonPublic))
                        accessorList.Add(m_otherMethod[i]);
                }
            }
            return accessorList.ToArray();
        }

        public override Type PropertyType
        {
            get { return Signature.ReturnType; }
        }

        public override MethodInfo GetGetMethod(bool nonPublic)
        {
            if (!Associates.IncludeAccessor(m_getterMethod, nonPublic))
                return null;

            return m_getterMethod;
        }

        public override MethodInfo GetSetMethod(bool nonPublic)
        {
            if (!Associates.IncludeAccessor(m_setterMethod, nonPublic))
                return null;

            return m_setterMethod;
        }

        public override ParameterInfo[] GetIndexParameters()
        {
            ParameterInfo[] indexParams = GetIndexParametersNoCopy();

            int numParams = indexParams.Length;

            if (numParams == 0)
                return indexParams;

            ParameterInfo[] ret = new ParameterInfo[numParams];

            Array.Copy(indexParams, ret, numParams);

            return ret;
        }

        internal ParameterInfo[] GetIndexParametersNoCopy()
        {
            // @History - Logic ported from RTM

            // No need to lock because we don't guarantee the uniqueness of ParameterInfo objects
            if (m_parameters == null)
            {
                int numParams = 0;
                ParameterInfo[] methParams = null;

                // First try to get the Get method.
                MethodInfo m = GetGetMethod(true);
                if (m != null)
                {
                    // There is a Get method so use it.
                    methParams = m.GetParametersNoCopy();
                    numParams = methParams.Length;
                }
                else
                {
                    // If there is no Get method then use the Set method.
                    m = GetSetMethod(true);

                    if (m != null)
                    {
                        methParams = m.GetParametersNoCopy();
                        numParams = methParams.Length - 1;
                    }
                }

                // Now copy over the parameter info's and change their 
                // owning member info to the current property info.

                ParameterInfo[] propParams = new ParameterInfo[numParams];

                for (int i = 0; i < numParams; i++)
                    propParams[i] = new RuntimeParameterInfo((RuntimeParameterInfo)methParams[i], this);

                m_parameters = propParams;
            }

            return m_parameters;
        }

        public override PropertyAttributes Attributes
        {
            get
            {
                return m_flags;
            }
        }

        public override bool CanRead
        {
            get
            {
                return m_getterMethod != null;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return m_setterMethod != null;
            }
        }
        #endregion

        #region Dynamic
        [DebuggerStepThroughAttribute]
        [Diagnostics.DebuggerHidden]
        public override Object GetValue(Object obj, Object[] index)
        {
            return GetValue(obj, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
                null, index, null);
        }

        [DebuggerStepThroughAttribute]
        [Diagnostics.DebuggerHidden]
        public override Object GetValue(Object obj, BindingFlags invokeAttr, Binder binder, Object[] index, CultureInfo culture)
        {
            MethodInfo m = GetGetMethod(true);
            if (m == null)
                throw new ArgumentException(System.SR.Arg_GetMethNotFnd);
            return m.Invoke(obj, invokeAttr, binder, index, null);
        }

        [DebuggerStepThroughAttribute]
        [Diagnostics.DebuggerHidden]
        public override void SetValue(Object obj, Object value, Object[] index)
        {
            SetValue(obj,
                    value,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static,
                    null,
                    index,
                    null);
        }

        [DebuggerStepThroughAttribute]
        [Diagnostics.DebuggerHidden]
        public override void SetValue(Object obj, Object value, BindingFlags invokeAttr, Binder binder, Object[] index, CultureInfo culture)
        {
            MethodInfo m = GetSetMethod(true);

            if (m == null)
                throw new ArgumentException(System.SR.Arg_SetMethNotFnd);

            Object[] args = null;

            if (index != null)
            {
                args = new Object[index.Length + 1];

                for (int i = 0; i < index.Length; i++)
                    args[i] = index[i];

                args[index.Length] = value;
            }
            else
            {
                args = new Object[1];
                args[0] = value;
            }

            m.Invoke(obj, invokeAttr, binder, args, culture);
        }
        #endregion

        #endregion

        #region ISerializable Implementation
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));
            Contract.EndContractBlock();

            MemberInfoSerializationHolder.GetSerializationInfo(info, this);
        }

        internal string SerializationToString()
        {
            return FormatNameAndSig(true);
        }
        #endregion
    }
}
