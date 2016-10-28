﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using System;
using System.IO;
using System.Text;
using System.Reflection;

public class CSGenerator
{
	// input
//	static StringBuilder sb = null;
	public static Type type = null;
	public static string thisClassName = null;
	
//	static string tempFile = JSBindingSettings.jsDir + "/temp" + JSBindingSettings.jsExtension;

	// used for record information
	public class ClassCallbackNames
	{
		// class type
		public Type type;
		
		public List<string> fields;
		public List<string> properties;
		public List<string> constructors;
		public List<string> methods;
		
		// genetated, generating CSParam code
		public List<string> constructorsCSParam;
		public List<string> methodsCSParam;
	}
	public static List<ClassCallbackNames> allClassCallbackNames;

    public static void OnBegin()
    {
        GeneratorHelp.ClearTypeInfo();
		
		if (Directory.Exists(JSBindingSettings.csGeneratedDir))
		{
			string[] files = Directory.GetFiles(JSBindingSettings.csGeneratedDir);
			for (int i = 0; i < files.Length; i++)
			{
				File.Delete(files[i]);
			}
		}
		else
		{
			Directory.CreateDirectory(JSBindingSettings.csGeneratedDir);
		}
	}
	public static void OnEnd()
	{
        
	}
	public static void Clear()
	{
		type = null;
//		sb = new StringBuilder();
	}
	public static TextFile BuildFields(Type type, FieldInfo[] fields, int[] fieldsIndex, ClassCallbackNames ccbn)
	{
		TextFile tfAll = new TextFile();
		for (int i = 0; i < fields.Length; i++)
		{
			TextFile tf = new TextFile();
			FieldInfo field = fields[i];
			bool isDelegate = JSDataExchangeEditor.IsDelegateDerived(field.FieldType);// (typeof(System.Delegate).IsAssignableFrom(field.FieldType));
			if (isDelegate)
			{
				tf.Add(JSDataExchangeEditor.Build_DelegateFunction(type, field, field.FieldType, i, 0).ToString());
			}

			bool bGenericT = type.IsGenericTypeDefinition;
			if (bGenericT)
			{
				tf.Add("public static FieldID fieldID{0} = new FieldID(\"{1}\");", i, field.Name);
			}
						
			JSDataExchangeEditor.MemberFeature features = 0;
			if (field.IsStatic) features |= JSDataExchangeEditor.MemberFeature.Static;
			
			string functionName = JSNameMgr.HandleFunctionName(type.Name + "_" + field.Name);
			TextFile tfFun = tf.Add("static void {0}(JSVCall vc)", functionName)
                .BraceIn();

			if (bGenericT)
			{
				tfFun.Add("FieldInfo member = GenericTypeCache.getField(vc.csObj.GetType(), fieldID{0});", i);
				tfFun.Add("if (member == null)").In().Add("return;").AddLine();
			}
			
			bool bReadOnly = (field.IsInitOnly || field.IsLiteral);
			TextFile tfGet = tfFun;
			if (!bReadOnly)
			{
				tfGet = tfFun.Add("if (vc.bGet)")
					.BraceIn();
			}

			tfGet.Add(JSDataExchangeEditor.BuildCallString(type, field, "" /* argList */,
			                                               features | JSDataExchangeEditor.MemberFeature.Get).Ch);
			
			tfGet.Add("{0}", JSDataExchangeEditor.Get_Return(field.FieldType, "result"));
			
			// set
			if (!bReadOnly)
			{
				TextFile tfSet = tfGet.BraceOut().Add("else").BraceIn();
				
				if (!isDelegate)
				{
					var paramHandler = JSDataExchangeEditor.Get_ParamHandler(field);
					tfSet.Add(paramHandler.getter);					
					tfSet.Add(JSDataExchangeEditor.BuildCallString(type, field, "" /* argList */,
					                                               features | JSDataExchangeEditor.MemberFeature.Set, paramHandler.argName).Ch);
				}
				else
				{
					var getDelegateFuncitonName = JSDataExchangeEditor.GetMethodArg_DelegateFuncionName(type, field.Name, i, 0);
					
					//                     sb.Append(JSDataExchangeEditor.BuildCallString(type, field, "" /* argList */,
					//                                 features | JSDataExchangeEditor.MemberFeature.Set, getDelegateFuncitonName + "(vc.getJSFunctionValue())"));
					
					string getDelegate = JSDataExchangeEditor.Build_GetDelegate(getDelegateFuncitonName, field.FieldType);
					tfSet.Add(JSDataExchangeEditor.BuildCallString(type, field, "" /* argList */,
					                                               features | JSDataExchangeEditor.MemberFeature.Set, getDelegate).Ch);
                }
				tfSet.BraceOut();
            }
            
			tfFun.BraceOut();
            ccbn.fields.Add(functionName);

			tfAll.Add(tf.Ch);
        }
        
        return tfAll;
	}
	public static void Type2TypeFlag(Type type, cg.args argFlag)
	{
		if (type.IsByRef)
		{
			argFlag.Add("TypeFlag.IsRef");
			type = type.GetElementType();
		}
		
		if (type.IsGenericParameter)
		{
			argFlag.Add("TypeFlag.IsT");
		}
		else if (type.IsGenericType)
		{
			argFlag.Add("TypeFlag.IsGenericType");
		}
		
		if (type.IsArray)
			argFlag.Add("TypeFlag.IsArray");
	}
	public static cg.args ParameterInfo2TypeFlag(ParameterInfo p)
	{
		cg.args argFlag = new cg.args();
		
		Type2TypeFlag(p.ParameterType, argFlag);
		
		if (p.IsOut)
			argFlag.Add("TypeFlag.IsOut");
		
		if (argFlag.Count == 0)
			argFlag.Add("TypeFlag.None");
		
		return argFlag;
	}
	public static TextFile BuildProperties(Type type, PropertyInfo[] properties, int[] propertiesIndex, ClassCallbackNames ccbn)
	{
		TextFile tfAll = new TextFile();
		for (int i = 0; i < properties.Length; i++)
		{
			TextFile tf = new TextFile();
			PropertyInfo property = properties[i];
			MethodInfo[] accessors = property.GetAccessors();
			bool isStatic = accessors[0].IsStatic;
			JSDataExchangeEditor.MemberFeature features = 0;
			if (isStatic) features |= JSDataExchangeEditor.MemberFeature.Static;
			
			bool bGenericT = type.IsGenericTypeDefinition;
			
			bool isDelegate = JSDataExchangeEditor.IsDelegateDerived(property.PropertyType); ;// (typeof(System.Delegate).IsAssignableFrom(property.PropertyType));
			if (isDelegate)
			{
				tf.Add(JSDataExchangeEditor.Build_DelegateFunction(type, property, property.PropertyType, i, 0).Ch);
			}
			
			// PropertyID
			if (bGenericT)
			{
				cg.args arg = new cg.args();
				arg.AddFormat("\"{0}\"", property.Name);
				
				arg.AddFormat("\"{0}\"", property.PropertyType.Name);
				if (property.PropertyType.IsGenericParameter)
				{
					arg.Add("TypeFlag.IsT");
				}
				else
				{
					arg.Add("TypeFlag.None");
				}
				
				cg.args arg1 = new cg.args();
				cg.args arg2 = new cg.args();
				
				foreach (ParameterInfo p in property.GetIndexParameters())
				{
					cg.args argFlag = ParameterInfo2TypeFlag(p);
					
					arg1.AddFormat("\"{0}\"", p.ParameterType.Name);                    
					arg2.Add(argFlag.Format(cg.args.ArgsFormat.Flag));
				}
				
				if (arg1.Count > 0)
					arg.AddFormat("new string[]{0}", arg1.Format(cg.args.ArgsFormat.Brace));
				else
					arg.Add("null");
				if (arg2.Count > 0)
					arg.AddFormat("new TypeFlag[]{0}", arg2.Format(cg.args.ArgsFormat.Brace));
				else
					arg.Add("null");

				tf.Add("public static PropertyID propertyID{0} = new PropertyID({1});", i, arg.ToString());
			}

			TextFile tft = null;
			if (bGenericT)
			{
				tft = new TextFile();
				tft.Add("PropertyInfo member = GenericTypeCache.getProperty(vc.csObj.GetType(), propertyID{0});", i);
				tft.Add("if (member == null)")
					.In().Add("return");
				tft.AddLine();
			}
			
			//
			// check to see if this is a indexer
			//
			ParameterInfo[] ps = property.GetIndexParameters();
			bool bIndexer = (ps.Length > 0);
			if (bIndexer) features |= JSDataExchangeEditor.MemberFeature.Indexer;
			cg.args argActual = new cg.args();
			JSDataExchangeEditor.ParamHandler[] paramHandlers = new JSDataExchangeEditor.ParamHandler[ps.Length];
			for (int j = 0; j < ps.Length; j++)
			{
				paramHandlers[j] = JSDataExchangeEditor.Get_ParamHandler(ps[j].ParameterType, j, false, false);
				argActual.Add(paramHandlers[j].argName);
			}
			
			string functionName = type.Name + "_" + property.Name;
			if (bIndexer)
			{
				foreach (var p in ps)
				{
					functionName += "_" + p.ParameterType.Name;
				}
			}
			functionName = JSNameMgr.HandleFunctionName(functionName);
			
			TextFile tfFun = tf.Add("static void {0}(JSVCall vc)", functionName).BraceIn();
			
			if (bGenericT)
			{
				tfFun.Add(tft.Ch);
			}
			for (int j = 0; j < ps.Length; j++)
			{
				tfFun.Add(paramHandlers[j].getter);
			}
			
			bool bReadOnly = (!property.CanWrite || property.GetSetMethod() == null);
			TextFile tfCall = JSDataExchangeEditor.BuildCallString(type, property, argActual.Format(cg.args.ArgsFormat.OnlyList), 
			                                                   features | JSDataExchangeEditor.MemberFeature.Get);

			TextFile tfGet = tfFun;
			if (!bReadOnly)
			{
				tfGet = tfFun.Add("if (vc.bGet)").BraceIn();
			}
			
			//if (type.IsValueType && !field.IsStatic)
			//    sb.AppendFormat("{0} argThis = ({0})vc.csObj;", type.Name);
			
			if (property.CanRead)
			{
				if (property.GetGetMethod() != null)
				{
					tfGet.Add(tfCall.Ch);
					tfGet.Add("{0}", JSDataExchangeEditor.Get_Return(property.PropertyType, "result"));
				}
				else
				{
					Debug.Log(type.Name + "." + property.Name + " 'get' is ignored because it's not public.");
				}
			}

			// set
			if (!bReadOnly)
			{
				TextFile tfSet = tfGet.BraceOut().Add("else").BraceIn();
				
				if (!isDelegate)
				{
					int ParamIndex = ps.Length;
					
					var paramHandler = JSDataExchangeEditor.Get_ParamHandler(property.PropertyType, ParamIndex, false, false);
					tfSet.Add(paramHandler.getter);
					
					tfSet.Add(JSDataExchangeEditor.BuildCallString(type, property, argActual.Format(cg.args.ArgsFormat.OnlyList),
					                                               features | JSDataExchangeEditor.MemberFeature.Set, paramHandler.argName).Ch);
				}
				else
				{
					var getDelegateFuncitonName = JSDataExchangeEditor.GetMethodArg_DelegateFuncionName(type, property.Name, i, 0);
					
					//                     sb.Append(JSDataExchangeEditor.BuildCallString(type, field, "" /* argList */,
					//                                 features | JSDataExchangeEditor.MemberFeature.Set, getDelegateFuncitonName + "(vc.getJSFunctionValue())"));
					
					string getDelegate = JSDataExchangeEditor.Build_GetDelegate(getDelegateFuncitonName, property.PropertyType);
					tfSet.Add(JSDataExchangeEditor.BuildCallString(type, property, "" /* argList */,
					                                               features | JSDataExchangeEditor.MemberFeature.Set, getDelegate).Ch);
				}
				tfSet.BraceOut();
			}
			
			tfFun.BraceOut();
			File.WriteAllText("D:\\22.txt", tf.Format(-1));
			tfAll.Add(tf.Ch);
			
			ccbn.properties.Add(functionName);
		}
		return tfAll;
	}
	public static string SharpKitTypeName(Type type)
	{
		string name = string.Empty;
		if (type.IsByRef)
		{
			name = SharpKitTypeName(type.GetElementType());
		}
		else if (type.IsArray)
		{
			while (type.IsArray)
			{
				Type subt = type.GetElementType();
				name += SharpKitTypeName(subt) + '$';
				type = subt;
			}
			name += "Array";
		}
		else if (type.IsGenericType)
		{
			name = type.Name;
			Type[] ts = type.GetGenericArguments();
			for (int i = 0; i < ts.Length; i++)
			{
				name += "$" + SharpKitTypeName(ts[i]);
			}
		}
		else
		{
			if (type == typeof(UnityEngine.Object))
				name = "UE" + type.Name;
			else
				name = type.Name;
		}
		return name;
		
	}
	public static string SharpKitMethodName(string methodName, ParameterInfo[] paramS, bool overloaded, int TCounts = 0)
	{
		string name = methodName;
		if (overloaded)
		{
			if (TCounts > 0)
				name += "T" + TCounts.ToString();
			for (int i = 0; i < paramS.Length; i++)
			{
				Type type = paramS[i].ParameterType;
				name += "$$" + SharpKitTypeName(type);
			}
			name = name.Replace("`", "T");
		}
		name = name.Replace("$", "_");
		return name;
	}
	public static TextFile BuildSpecialFunctionCall(ParameterInfo[] ps, string className, string methodName, bool bStatic, bool returnVoid, Type returnType)
	{
		TextFile tf = new TextFile();
		var paramHandlers = new JSDataExchangeEditor.ParamHandler[ps.Length];
		for (int i = 0; i < ps.Length; i++)
		{
			paramHandlers[i] = JSDataExchangeEditor.Get_ParamHandler(ps[i], i);
			tf.Add(paramHandlers[i].getter);
		}
		
		string strCall = string.Empty;
		
		// must be static
		if (methodName == "op_Addition")
			strCall = paramHandlers[0].argName + " + " + paramHandlers[1].argName;
		else if (methodName == "op_Subtraction")
			strCall = paramHandlers[0].argName + " - " + paramHandlers[1].argName;
		else if (methodName == "op_Multiply")
			strCall = paramHandlers[0].argName + " * " + paramHandlers[1].argName;
		else if (methodName == "op_Division")
			strCall = paramHandlers[0].argName + " / " + paramHandlers[1].argName;
		else if (methodName == "op_Equality")
			strCall = paramHandlers[0].argName + " == " + paramHandlers[1].argName;
		else if (methodName == "op_Inequality")
			strCall = paramHandlers[0].argName + " != " + paramHandlers[1].argName;
		
		else if (methodName == "op_UnaryNegation")
			strCall = "-" + paramHandlers[0].argName;
		
		else if (methodName == "op_LessThan")
			strCall = paramHandlers[0].argName + " < " + paramHandlers[1].argName;
		else if (methodName == "op_LessThanOrEqual")
			strCall = paramHandlers[0].argName + " <= " + paramHandlers[1].argName;
		else if (methodName == "op_GreaterThan")
			strCall = paramHandlers[0].argName + " > " + paramHandlers[1].argName;
		else if (methodName == "op_GreaterThanOrEqual")
			strCall = paramHandlers[0].argName + " >= " + paramHandlers[1].argName;
		else if (methodName == "op_Implicit")
			strCall = "(" + JSNameMgr.GetTypeFullName(returnType) + ")" + paramHandlers[0].argName;
		else
			Debug.LogError("Unknown special name: " + methodName);
		
		string ret = JSDataExchangeEditor.Get_Return(returnType, strCall);
		tf.Add(ret);
		return tf;
	}
	public static TextFile BuildNormalFunctionCall(
		int methodTag, 
		ParameterInfo[] ps,
		string methodName, 
		bool bStatic, 
		Type returnType, 
		bool bConstructor,
		int TCount = 0)
	{
		TextFile tf = new TextFile();
		
		if (bConstructor)
		{
			tf.Add("int _this = JSApi.getObject((int)JSApi.GetType.Arg);");
			tf.Add("JSApi.attachFinalizerObject(_this);");
			tf.Add("--argc;").AddLine();
		}
		
		if (bConstructor)
		{
			if (type.IsGenericTypeDefinition)
			{
				// Not generic method, but is generic type
				
				tf.Add("ConstructorInfo constructor = JSDataExchangeMgr.makeGenericConstructor(typeof({0}), constructorID{1});",
				                 JSNameMgr.GetTypeFullName(type), methodTag);
				
				//sbMethodHitTest.AppendFormat("GenericTypeCache.getConstructor(typeof({0}), {2}.constructorID{1});\n", JSNameMgr.GetTypeFullName(type), methodTag, JSNameMgr.GetTypeFileName(type));
				
				tf.Add("if (constructor == null)").In().Add("return true;").Out().AddLine();
			}
		}
		
		else if (TCount > 0)
		{
			tf.Add("// Get generic method by name and param count.");
			
			if (!bStatic) // instance method
			{
				tf.Add("MethodInfo method = JSDataExchangeMgr.makeGenericMethod(vc.csObj.GetType(), methodID{0}, {1});",
				                 methodTag,
				                 TCount);
			}
			else // static method
			{
				tf.Add("MethodInfo method = JSDataExchangeMgr.makeGenericMethod(typeof({0}), methodID{1}, {2});",
				                 JSNameMgr.GetTypeFullName(type),
				                 methodTag,
				                 TCount);
			}
			tf.Add("if (method == null)").In().Add("return true;").Out().AddLine();
		}
		else if (type.IsGenericTypeDefinition)
		{
			// not generic method, but is generic type
			tf.Add("// Get generic method by name and param count.");
			
			if (!bStatic) // instance method
			{
				tf.Add("MethodInfo method = GenericTypeCache.getMethod(vc.csObj.GetType(), methodID{0});", methodTag);
			}
			else // static method
			{
				// Debug.LogError("=================================ERROR");
				tf.Add("MethodInfo method = GenericTypeCache.getMethod(typeof({0}), methodID{1});",
				                 JSNameMgr.GetTypeFullName(type), // [0]
				                 methodTag);
			}
			tf.Add("if (method == null)").In().Add("return true;").Out().AddLine();
		}
		else if (type.IsGenericType)
		{
			// error
		}
		
		var paramHandlers = new JSDataExchangeEditor.ParamHandler[ps.Length];        
		for (int i = 0; i < ps.Length; i++)
		{
			if (true /* !ps[i].ParameterType.IsGenericParameter */ )
			{
				// use original method's parameterinfo
				if (!JSDataExchangeEditor.IsDelegateDerived(ps[i].ParameterType))
					paramHandlers[i] = JSDataExchangeEditor.Get_ParamHandler(ps[i], i);
				//                if (ps[i].ParameterType.IsGenericParameter)
				//                {
				//                    paramHandlers[i].getter = "    JSMgr.datax.setTemp(method.GetParameters()[" + i.ToString() + "].ParameterType);\n" + paramHandlers[i].getter;
				//                }
			}
		}
		
		// minimal params needed
		int minNeedParams = 0;
		for (int i = 0; i < ps.Length; i++)
		{
			if (ps[i].IsOptional) { break; }
			minNeedParams++;
		}
		
		
		if (bConstructor && type.IsGenericTypeDefinition)
			tf.Add("int len = argc - {0};", type.GetGenericArguments().Length);
		else if (TCount == 0)
			tf.Add("int len = argc;");
		else
			tf.Add("int len = argc - {0};", TCount);
		
		for (int j = minNeedParams; j <= ps.Length; j++)
		{
			TextFile tfGetParam = new TextFile();
			StringBuilder sbActualParam = new StringBuilder();
			TextFile tfUpdateRefParam = new TextFile();
			
			// receive arguments first
			for (int i = 0; i < j; i++)
			{
				ParameterInfo p = ps[i];
				//if (typeof(System.Delegate).IsAssignableFrom(p.ParameterType))
				if (JSDataExchangeEditor.IsDelegateDerived(p.ParameterType))
				{
					//string delegateGetName = JSDataExchangeEditor.GetFunctionArg_DelegateFuncionName(className, methodName, methodIndex, i);
					string delegateGetName = JSDataExchangeEditor.GetMethodArg_DelegateFuncionName(type, methodName, methodTag, i);
					
					//if (p.ParameterType.IsGenericType)
					if (p.ParameterType.ContainsGenericParameters)
					{
						// cg.args ta = new cg.args();
						// sbGetParam.AppendFormat("foreach (var a in method.GetParameters()[{0}].ParameterType.GetGenericArguments()) ta.Add();");
												
						TextFile tfAction = tfGetParam.Add("object arg{0} = JSDataExchangeMgr.GetJSArg<object>(() =>", i).BraceIn();
						{
							TextFile tfIf = tfAction.Add("if (JSApi.isFunctionS((int)JSApi.GetType.Arg))").BraceIn();
							{
								tfIf.Add("var getDelegateFun{0} = typeof({1}).GetMethod(\"{2}\").MakeGenericMethod", i, thisClassName, delegateGetName)
									.In().Add("(method.GetParameters()[{0}].ParameterType.GetGenericArguments());", i)
									.Out().Add("return getDelegateFun{0}.Invoke(null, new object[]{{{1}}});", i, "JSApi.getFunctionS((int)JSApi.GetType.Arg)");

								tfIf.BraceOut();
							}
							TextFile tfElse = tfAction.Add("else").BraceIn();
							{
								tfElse.Add("return JSMgr.datax.getObject((int)JSApi.GetType.Arg);");

								tfElse.BraceOut();
							}

							tfAction.BraceOut();
						}
					}   
					else
					{
						tfGetParam.Add("{0} arg{1} = {2};",
						                        JSNameMgr.GetTypeFullName(p.ParameterType), // [0]
						                        i, // [1]
						                        JSDataExchangeEditor.Build_GetDelegate(delegateGetName, p.ParameterType) // [2]
						                        );
					}
				}
				else
				{
					tfGetParam.Add(paramHandlers[i].getter);
				}
				
				// value type array
				// no 'out' nor 'ref'
				if ((p.ParameterType.IsByRef || p.IsOut) && !p.ParameterType.IsArray)
					sbActualParam.AppendFormat("{0} arg{1}{2}", (p.IsOut) ? "out" : "ref", i, (i == j - 1 ? "" : ", "));
				else
					sbActualParam.AppendFormat("arg{0}{1}", i, (i == j - 1 ? "" : ", "));
				
				// updater
				tfUpdateRefParam.Add(paramHandlers[i].updater);
			}
			
			/*
             * 0 parameters count
             * 1 class name
             * 2 function name
             * 3 actual parameters
             */
			if (bConstructor)
			{
				StringBuilder sbCall = new StringBuilder();
				
				if (!type.IsGenericTypeDefinition)
					sbCall.AppendFormat("new {0}({1})", JSNameMgr.GetTypeFullName(type), sbActualParam.ToString());
				else
				{
					sbCall.AppendFormat("constructor.Invoke(null, new object[]{{{0}}})", sbActualParam);
				}
				
				// string callAndReturn = JSDataExchangeEditor.Get_Return(type/*don't use returnType*/, sbCall.ToString());
				string callAndReturn = new StringBuilder().AppendFormat("JSMgr.addJSCSRel(_this, {0});", sbCall).ToString();

				TextFile tfIf = tf.Add("{0}if (len == {1})", (j == minNeedParams) ? "" : "else ", j).BraceIn();
				{
					tfIf.Add(tfGetParam.Ch);
					tfIf.Add(callAndReturn);
					if (tfUpdateRefParam.Ch.Count > 0)
						tfIf.Add(tfUpdateRefParam.Ch);
					tfIf.BraceOut();
				}
			}
			else
			{
				StringBuilder sbCall = new StringBuilder();
				StringBuilder sbActualParamT_arr = new StringBuilder();
				//StringBuilder sbUpdateRefT = new StringBuilder();
				
				if (TCount == 0 && !type.IsGenericTypeDefinition)
				{
					if (bStatic)
						sbCall.AppendFormat("{0}.{1}({2})", JSNameMgr.GetTypeFullName(type), methodName, sbActualParam.ToString());
					else if (!type.IsValueType)
						sbCall.AppendFormat("(({0})vc.csObj).{1}({2})", JSNameMgr.GetTypeFullName(type), methodName, sbActualParam.ToString());
					else
						sbCall.AppendFormat("argThis.{0}({1})", methodName, sbActualParam.ToString());
				}
				else
				{
					if (ps.Length > 0)
					{
						sbActualParamT_arr.AppendFormat("object[] arr_t = new object[]{{{0}}};", sbActualParam);
						// reflection call doesn't need out or ref modifier
						sbActualParamT_arr.Replace(" out ", " ").Replace(" ref ", " ");
					}
					else
					{
						sbActualParamT_arr.Append("object[] arr_t = null;");
					}
					
					if (bStatic)
						sbCall.AppendFormat("method.Invoke(null, arr_t)");
					else if (!type.IsValueType)
						sbCall.AppendFormat("method.Invoke(vc.csObj, arr_t)");
					else
						sbCall.AppendFormat("method.Invoke(vc.csObj, arr_t)");
				}
				
				string callAndReturn = JSDataExchangeEditor.Get_Return(returnType, sbCall.ToString());
				
				StringBuilder sbStruct = null;
				if (type.IsValueType && !bStatic && TCount == 0 && !type.IsGenericTypeDefinition)
				{
					sbStruct = new StringBuilder();
					sbStruct.AppendFormat("{0} argThis = ({0})vc.csObj;", JSNameMgr.GetTypeFullName(type));
				}
				
				TextFile tfIf = tf.Add("{0}if (len == {1})", (j == minNeedParams) ? "" : "else ", j).BraceIn();
				{
					tfIf.Add(tfGetParam.Ch);
					if (sbActualParamT_arr.Length > 0)
					{
						tfIf.Add(sbActualParamT_arr.ToString());
					}
					
					// if it is Struct, get argThis first
					if (type.IsValueType && !bStatic && TCount == 0 && !type.IsGenericTypeDefinition)
					{
						tfIf.Add(sbStruct.ToString());
					}
					
					tfIf.Add(callAndReturn);
					
					// if it is Struct, update 'this' object
					if (type.IsValueType && !bStatic && TCount == 0 && !type.IsGenericTypeDefinition)
					{
						tfIf.Add("JSMgr.changeJSObj(vc.jsObjID, argThis);");
					}
					tfIf.Add(tfUpdateRefParam.Ch);
					tfIf.BraceOut();
				}
            }
		}
		
		return tf;
	}
	static StringBuilder GenListCSParam2(ParameterInfo[] ps)
	{
		StringBuilder sb = new StringBuilder();
		
		string fmt = "new JSVCall.CSParam({0}, {1}, {2}, {3}{4}, {5}), ";
		for (int i = 0; i < ps.Length; i++)
		{
			ParameterInfo p = ps[i];
			Type t = p.ParameterType;
			sb.AppendFormat(fmt, t.IsByRef ? "true" : "false", p.IsOptional ? "true" : "false", t.IsArray ? "true" : "false", "typeof(" + JSNameMgr.GetTypeFullName(t) + ")", t.IsByRef ? ".MakeByRefType()" : "", "null");
		}
		fmt = "new JSVCall.CSParam[][{0}]";
		StringBuilder sbX = new StringBuilder();
		sbX.AppendFormat(fmt, sb);
		return sbX;
	}
	public static TextFile BuildMethods(Type type, MethodInfo[] methods, int[] methodsIndex, int[] olInfo, ClassCallbackNames ccbn)
	{
		TextFile tfAll = new TextFile();
		for (int i = 0; i < methods.Length; i++)
		{
			TextFile tf = new TextFile();
			MethodInfo method = methods[i];
			ParameterInfo[] paramS = method.GetParameters();
			
			for (int j = 0; j < paramS.Length; j++)
			{
				//                 if (paramS[j].ParameterType == typeof(DaikonForge.Tween.TweenAssignmentCallback<Vector3>))
				//                 {
				//                     Debug.Log("yes");
				//                
				//if (typeof(System.Delegate).IsAssignableFrom(paramS[j].ParameterType))
				if (JSDataExchangeEditor.IsDelegateDerived(paramS[j].ParameterType))
				{
					// StringBuilder sbD = JSDataExchangeEditor.BuildFunctionArg_DelegateFunction(type.Name, method.Name, paramS[j].ParameterType, i, j);
					TextFile tfD = JSDataExchangeEditor.Build_DelegateFunction(type, method, paramS[j].ParameterType, i, j);					
					tf.Add(tfD.Ch);
				}
			}
			
			// MethodID
			if (type.IsGenericTypeDefinition || method.IsGenericMethodDefinition)
			{
				cg.args arg = new cg.args();
				arg.AddFormat("\"{0}\"", method.Name);
				
				arg.AddFormat("\"{0}\"", method.ReturnType.Name);
				if (method.ReturnType.IsGenericParameter)
				{
					arg.Add("TypeFlag.IsT");
				}
				else
				{
					arg.Add("TypeFlag.None");
				}
				
				cg.args arg1 = new cg.args();
				cg.args arg2 = new cg.args();
				
				foreach (ParameterInfo p in method.GetParameters())
				{
					// flag of a parameter
					cg.args argFlag = ParameterInfo2TypeFlag(p);
					
					arg1.AddFormat("\"{0}\"", p.ParameterType.Name);
					arg2.Add(argFlag.Format(cg.args.ArgsFormat.Flag));
				}
				
				if (arg1.Count > 0)
					arg.AddFormat("new string[]{0}", arg1.Format(cg.args.ArgsFormat.Brace));
				else
					arg.Add("null");
				if (arg2.Count > 0)
					arg.AddFormat("new TypeFlag[]{0}", arg2.Format(cg.args.ArgsFormat.Brace));
				else
					arg.Add("null");
				tf.Add("public static MethodID methodID{0} = new MethodID({1});", i, arg.ToString());
			}
			
			int olIndex = olInfo[i];
			bool returnVoid = (method.ReturnType == typeof(void));
			
			string functionName = type.Name + "_" + method.Name + (olIndex > 0 ? olIndex.ToString() : "") + (method.IsStatic ? "_S" : "");
			
			int TCount = 0;
			if (method.IsGenericMethodDefinition)
				TCount = method.GetGenericArguments().Length;
			
			// if you change functionName
			// also have to change code in 'Manual/' folder
			functionName = JSNameMgr.HandleFunctionName(type.Name + "_" + SharpKitMethodName(method.Name, paramS, true, TCount));
			if (method.IsSpecialName && method.Name == "op_Implicit" && paramS.Length > 0)
			{
				functionName += "_to_" + method.ReturnType.Name;
			}

			TextFile tfFun = tf.Add("static bool {0}(JSVCall vc, int argc)", functionName)
				.BraceIn();

			if (UnityEngineManual.isManual(functionName))
			{
				tfFun.Add("UnityEngineManual.{0}(vc, argc);", functionName);
			}
			else if (!JSBindingSettings.IsSupportByDotNet2SubSet(functionName))
			{
				tfFun.Add("UnityEngine.Debug.LogError(\"This method is not supported by .Net 2.0 subset.\");");
			}
			else
			{
				tfFun.Add(method.IsSpecialName ? BuildSpecialFunctionCall(paramS, type.Name, method.Name, method.IsStatic, returnVoid, method.ReturnType).Ch
				                : BuildNormalFunctionCall(i, paramS, method.Name, method.IsStatic, method.ReturnType, 
				                          false/* is constructor */, 
				                          TCount).Ch);
			}
            tfFun.Add("return true;");
			tfFun.BraceOut();
			tfAll.Add(tf.Ch);
			
			ccbn.methods.Add(functionName);
			ccbn.methodsCSParam.Add(GenListCSParam2(paramS).ToString());
		}
		return tfAll;
	}
    public static TextFile BuildConstructors(Type type, ConstructorInfo[] constructors, int[] constructorsIndex, ClassCallbackNames ccbn)
    {
        TextFile tfAll = new TextFile();
        // increase index if adding default constructor
        //         int deltaIndex = 0;
        if (JSBindingSettings.NeedGenDefaultConstructor(type))
        {
            //             deltaIndex = 1;
        }

        for (int i = 0; i < constructors.Length; i++)
        {
            TextFile tf = new TextFile();
            ConstructorInfo cons = constructors[i];

            if (cons == null)
            {
                tf.Add("public static ConstructorID constructorID{0} = new ConstructorID({1});", i, "null, null").AddLine();

                // this is default constructor
                //bool returnVoid = false;
                //string functionName = type.Name + "_" + type.Name + "1";
                int olIndex = i + 1; // for constuctors, they are always overloaded
                string functionName = JSNameMgr.HandleFunctionName(type.Name + "_" + type.Name + (olIndex > 0 ? olIndex.ToString() : ""));

                TextFile tfFun = tf.Add("static bool {0}(JSVCall vc, int argc)", functionName).BraceIn();
                tfFun.Add(BuildNormalFunctionCall(0, new ParameterInfo[0], type.Name, false, null, true).Ch);
                tfFun.BraceOut();
                ccbn.constructors.Add(functionName);
                ccbn.constructorsCSParam.Add(GenListCSParam2(new ParameterInfo[0]).ToString());
            }
            else
            {
                ParameterInfo[] paramS = cons.GetParameters();
                int olIndex = i + 1; // for constuctors, they are always overloaded
                int methodTag = i/* + deltaIndex*/;

                for (int j = 0; j < paramS.Length; j++)
                {
                    if (JSDataExchangeEditor.IsDelegateDerived(paramS[j].ParameterType))
                    {
                        TextFile tfD = JSDataExchangeEditor.Build_DelegateFunction(type, cons, paramS[j].ParameterType, methodTag, j);
                        tf.Add(tfD.Ch);
                    }
                }

                // ConstructorID
                if (type.IsGenericTypeDefinition)
                {
                    cg.args arg = new cg.args();
                    cg.args arg1 = new cg.args();
                    cg.args arg2 = new cg.args();

                    foreach (ParameterInfo p in cons.GetParameters())
                    {
                        cg.args argFlag = ParameterInfo2TypeFlag(p);
                        arg1.AddFormat("\"{0}\"", p.ParameterType.Name);
                        arg2.Add(argFlag.Format(cg.args.ArgsFormat.Flag));
                    }

                    if (arg1.Count > 0)
                        arg.AddFormat("new string[]{0}", arg1.Format(cg.args.ArgsFormat.Brace));
                    else
                        arg.Add("null");
                    if (arg2.Count > 0)
                        arg.AddFormat("new TypeFlag[]{0}", arg2.Format(cg.args.ArgsFormat.Brace));
                    else
                        arg.Add("null");
                    tf.Add("public static ConstructorID constructorID{0} = new ConstructorID({1});", i, arg.ToString()).AddLine();
                }

                string functionName = JSNameMgr.HandleFunctionName(type.Name + "_" + type.Name + (olIndex > 0 ? olIndex.ToString() : "") + (cons.IsStatic ? "_S" : ""));

                TextFile tfFun = tf.Add("static bool {0}(JSVCall vc, int argc)", functionName).BraceIn();
                {
                    tfFun.Add(BuildNormalFunctionCall(methodTag, paramS, cons.Name, cons.IsStatic, null, true, 0).Ch);
                    tfFun.Add("return true;");
                    tfFun.BraceOut();
                }

                ccbn.constructors.Add(functionName);
                ccbn.constructorsCSParam.Add(GenListCSParam2(paramS).ToString());

                tfAll.Add(tf.Ch);
            }
        }
        return tfAll;
    }
    static TextFile BuildRegisterFunction(ClassCallbackNames ccbn, GeneratorHelp.ATypeInfo ti)
    {
        TextFile tf = new TextFile();
        TextFile tfFun = tf.Add("public static void __Register()").BraceIn();
        {
            tfFun.Add("JSMgr.CallbackInfo ci = new JSMgr.CallbackInfo();");
            tfFun.Add("ci.type = typeof({0});", JSNameMgr.GetTypeFullName(ccbn.type));

            TextFile tfFields = tfFun.Add("ci.fields = new JSMgr.CSCallbackField[]").BraceIn();
            {
                for (int i = 0; i < ccbn.fields.Count; i++)
                    tfFields.Add("{0},", ccbn.fields[i]);

                tfFields.BraceOutSC();
            }

            TextFile tfProperties = tfFun.Add("ci.properties = new JSMgr.CSCallbackProperty[]").BraceIn();
            {
                for (int i = 0; i < ccbn.properties.Count; i++)
                    tfProperties.Add("{0},", ccbn.properties[i]);

                tfProperties.BraceOutSC();
            }

            TextFile tfConstructors = tfFun.Add("ci.constructors = new JSMgr.MethodCallBackInfo[]").BraceIn();
            {
                for (int i = 0; i < ccbn.constructors.Count; i++)
                {
                    if (ccbn.constructors.Count == 1 && ti.constructors.Length == 0) // no constructors   add a default  so ...
                        tfConstructors.Add("new JSMgr.MethodCallBackInfo({0}, \"{1}\"),",
                            ccbn.constructors[i],
                            type.Name);
                    else
                        tfConstructors.Add("new JSMgr.MethodCallBackInfo({0}, \"{1}\"),",
                            ccbn.constructors[i],
                            ti.constructors[i] == null ? ".ctor" : ti.constructors[i].Name);
                }

                tfConstructors.BraceOutSC();
            }

            TextFile tfMethods = tfFun.Add("ci.methods = new JSMgr.MethodCallBackInfo[]").BraceIn();
            {
                for (int i = 0; i < ccbn.methods.Count; i++)
                {
                    // if method is not overloaded
                    // don's save the cs param array
                    tfMethods.Add("new JSMgr.MethodCallBackInfo({0}, \"{1}\"),",
                        ccbn.methods[i],
                        ti.methods[i].Name);
                }

                tfMethods.BraceOutSC();
            }
            tfFun.Add("JSMgr.allCallbackInfo.Add(ci);");
        }
        tfFun.BraceOut();
        return tf;
    }
    public static TextFile BuildFile(Type type,
        TextFile tfFields,
        TextFile tfProperties,
        TextFile tfMethods,
        TextFile tfConstructors,
        TextFile tfRegister)
    {
        TextFile tfFile = new TextFile();
        tfFile.Add("using UnityEngine;");
        tfFile.Add("using System;");
        tfFile.Add("using System.Collections;");
        tfFile.Add("using System.Collections.Generic;");
        tfFile.Add("using System.IO;");
        tfFile.Add("using System.Reflection;");

        if (type.Namespace != null)
        {
            string ns = type.Namespace;
            if (!(ns == "UnityEngine"
                || ns == "System"
                || ns == "System.Collections"
                || ns == "System.Collections.Generic"
                || ns == "System.IO"
                || ns == "System.Reflection"))
            {
                tfFile.Add("using {0};", ns);
            }
        }
        tfFile.Add("using jsval = JSApi.jsval;");
        tfFile.Add("public class {0}", thisClassName);
        TextFile tfClass = tfFile.BraceIn();
        {
            tfClass.Add("////////////////////// {0} ///////////////////////////////////////", type.Name);
            tfClass.Add("// constructors").Add(tfConstructors.Ch);
            tfClass.Add("// fields").Add(tfFields.Ch);
            tfClass.Add("// properties").Add(tfProperties.Ch);
            tfClass.Add("// methods").Add(tfMethods.Ch).AddLine();
            tfClass.Add("// register").Add(tfRegister.Ch);

            tfClass.BraceOut();
        }

        return tfFile;
    }

    static StreamWriter OpenFile(string fileName, bool bAppend = false)
    {
        return new StreamWriter(fileName, bAppend, Encoding.UTF8);
    }
    public static void GenerateClass()
    {
        GeneratorHelp.ATypeInfo ti;
        GeneratorHelp.AddTypeInfo(type, out ti);
        ClassCallbackNames ccbn = new ClassCallbackNames();
        {
            ccbn.type = type;
            ccbn.fields = new List<string>(ti.fields.Length);
            ccbn.properties = new List<string>(ti.properties.Length);
            ccbn.constructors = new List<string>(ti.constructors.Length);
            ccbn.methods = new List<string>(ti.methods.Length);
            
            ccbn.constructorsCSParam = new List<string>(ti.constructors.Length);
			ccbn.methodsCSParam = new List<string>(ti.methods.Length);
        }
		
		thisClassName = JSNameMgr.GetTypeFileName(type) + "_G";
		var tfFields = BuildFields(type, ti.fields, ti.fieldsIndex, ccbn);
		var tfProperties = BuildProperties(type, ti.properties, ti.propertiesIndex, ccbn);
		var tfMethods = BuildMethods(type, ti.methods, ti.methodsIndex, ti.methodsOLInfo, ccbn);
        var tfCons = BuildConstructors(type, ti.constructors, ti.constructorsIndex, ccbn);
        var tfRegister = BuildRegisterFunction(ccbn, ti);
        var tfClass = BuildFile(type, tfFields, tfProperties, tfMethods, tfCons, tfRegister);

        string fileName = string.Format("{0}/{1}_G.cs", JSBindingSettings.csGeneratedDir, JSNameMgr.GetTypeFileName(type));
        var w = OpenFile(fileName, false);
        w.Write(tfClass.Format(-1));
        w.Close();
    }
    public static void GenerateRegisterAll()
    {
        TextFile tf = new TextFile();
        tf.Add("using UnityEngine;");
        tf.Add("public class CSharpGenerated");
        TextFile tfClass = tf.BraceIn();
        {
            tfClass.Add("public static void RegisterAll()");
            TextFile tfFun = tfClass.BraceIn();
            {
                tfFun.Add("if (JSMgr.allCallbackInfo.Count != 0)")
                    .BraceIn()
                    .Add("Debug.LogError(999777454);")
                    .BraceOut();

                tfFun.AddLine();
                for (int i = 0; i < JSBindingSettings.classes.Length; i++)
                {
                    tfFun.Add("{0}_G.__Register();", JSNameMgr.GetTypeFileName(JSBindingSettings.classes[i]));
                }

                tfFun.BraceOut();
            }

            tfClass.BraceOut();
        }
        string fileName = string.Format("{0}/CSharp_G.cs", JSBindingSettings.csGeneratedDir);
        var w = OpenFile(fileName, false);
        w.Write(tf.Format(-1));
        w.Close();
    }
    public static void GenerateClassBindings()
    {
        CSGenerator.OnBegin();
		allClassCallbackNames = null;
		allClassCallbackNames = new List<ClassCallbackNames>(JSBindingSettings.classes.Length);
		for (int i = 0; i < JSBindingSettings.classes.Length; i++)
		{
			CSGenerator.Clear();
			CSGenerator.type = JSBindingSettings.classes[i];
			CSGenerator.GenerateClass();
		}
		//GenerateRegisterAll();
		//GenerateAllJSFileNames();

        GenerateRegisterAll();
		CSGenerator.OnEnd();
		
		Debug.Log("Generate CS Bindings OK. total = " + JSBindingSettings.classes.Length.ToString());
    }
    
    public static bool CheckClassBindings()
    {
        Dictionary<Type, bool> clrLibrary = new Dictionary<Type, bool>();
		{
			//
			// these types are defined in clrlibrary.javascript
			//
			clrLibrary.Add(typeof(System.Object), true);
			clrLibrary.Add(typeof(System.Exception), true);
			clrLibrary.Add(typeof(System.SystemException), true);
			clrLibrary.Add(typeof(System.ValueType), true);
		}
		
		Dictionary<Type, bool> dict = new Dictionary<Type, bool>();
		var sb = new StringBuilder();
		bool ret = true;
		
		// can not export a type twice
		foreach (var type in JSBindingSettings.classes)
		{
			if (typeof(System.Delegate).IsAssignableFrom(type))
			{
				sb.AppendFormat("\"{0}\" Delegate 不能导出.\n",
				                JSNameMgr.GetTypeFullName(type));
				ret = false;
			}

			// TODO
//			if (JSSerializerEditor.WillTypeBeTranslatedToJavaScript(type))
//			{
//				sb.AppendFormat("\"{0}\" has JsType attribute, it can not be in JSBindingSettings.classes at the same time.\n", 
//				                JSNameMgr.GetTypeFullName(type));
//				ret = false;
//			}
			
			if (type.IsGenericType && !type.IsGenericTypeDefinition)
			{
				sb.AppendFormat(
					"\"{0}\" 不能导出。 尝试换成 \"{1}\".\n",
					JSNameMgr.GetTypeFullName(type), JSNameMgr.GetTypeFullName(type.GetGenericTypeDefinition()));
				ret = false;
			}
			
			if (dict.ContainsKey(type))
			{
				sb.AppendFormat(
					"JSBindingSettings.classes 有不止1个 \"{0}\"。\n",
					JSNameMgr.GetTypeFullName(type));
				ret = false;
			}
			else
			{
				dict.Add(type, true);
			}
		}
		
		// 基类导出了吗？基类也得导出的
		foreach (var typeb in dict)
		{
			Type type = typeb.Key;
			Type baseType = type.BaseType;
			if (baseType == null)
			{
				continue;
			}

			if (baseType.IsGenericType) 
				baseType = baseType.GetGenericTypeDefinition();

			// System.Object is already defined in SharpKit clrlibrary
			if (!clrLibrary.ContainsKey(baseType) && !dict.ContainsKey(baseType))
			{
				sb.AppendFormat("\"{0}\" 的基类 \"{1}\" 也得导出。\n",
				                JSNameMgr.GetTypeFullName(type),
				                JSNameMgr.GetTypeFullName(baseType));
				ret = false;
			}
			
			// 检查 interface 有没有配置		
			Type[] interfaces = type.GetInterfaces();
			for (int i = 0; i < interfaces.Length; i++)
			{
				Type ti = interfaces[i];
				
				string tiFullName = JSNameMgr.GetTypeFullName(ti);
				
				// 这个检查有点奇葩
				// 有些接口带 <>，这里直接忽略，不检查他
				if (!tiFullName.Contains("<") && tiFullName.Contains(">") && 
				    !clrLibrary.ContainsKey(ti) && !dict.ContainsKey(ti))
				{
					sb.AppendFormat("\"{0}\"\'s interface \"{1}\" must also be in JSBindingSettings.classes.\n",
					                JSNameMgr.GetTypeFullName(type),
					                JSNameMgr.GetTypeFullName(ti));
					ret = false;
				}
			}
			
			//             foreach (var Interface in type.GetInterfaces())
			//             {
			//                 if (!dict.ContainsKey(Interface))
			//                 {
			//                     sb.AppendFormat("Interface \"{0}\" of \"{1}\" must also be in JSBindingSettings.classes.",
			//                         JSNameMgr.GetTypeFullName(Interface),
			//                         JSNameMgr.GetTypeFullName(type));
			//                     Debug.LogError(sb);
			//                     return false;
			//                 }
			//             }
		}
		if (!ret)
		{
			Debug.LogError(sb);
		}
		return ret;
	}

}
