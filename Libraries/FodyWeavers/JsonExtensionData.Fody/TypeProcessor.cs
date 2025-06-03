using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;

namespace JsonExtensionData.Fody;

public partial class ModuleWeaver
{
    private TypeReference? _dictionaryStringObjectReference;
    // private MethodReference? _dictionaryStringObjectConstructorReference;
    private MethodReference? _jsonExtensionDataAttributeReference;

    public void ProcessType(TypeDefinition typeDefinition)
    {
        if (typeDefinition.BaseType != null && typeDefinition.BaseType.FullName != "System.Object")
        {
            return;
        }

        // If the property already has somewhere on the inheritance chain the JsonExtensionDataAttribute, dont add it again
        if (FieldsFlattened(typeDefinition).Any(f =>
                f.CustomAttributes.Any(a => a.AttributeType.FullName.Contains("JsonExtensionDataAttribute"))))
        {
            return;
        }

        if (_dictionaryStringObjectReference == null)
        {
            var mscorlibModule = AssemblyResolver.Resolve(AssemblyNameReference.Parse("mscorlib, Version=4.0.0.0")).MainModule;

            var dictionaryDefinition = new GenericInstanceType(mscorlibModule.ExportedTypes.Single(t => t.FullName == "System.Collections.Generic.Dictionary`2").Resolve());
            var stringReference = mscorlibModule.ExportedTypes.Single(t => t.FullName == "System.String").Resolve();
            var objectReference = mscorlibModule.ExportedTypes.Single(t => t.FullName == "System.Object").Resolve();
            dictionaryDefinition.GenericArguments.Add(stringReference);
            dictionaryDefinition.GenericArguments.Add(objectReference);

            /*
            var constructor = new GenericInstanceMethod(dictionaryDefinition.Resolve().Methods.First(m => m.IsConstructor && !m.HasParameters));
            constructor.GenericArguments.Add(stringReference);
            constructor.GenericArguments.Add(objectReference);
            constructor.GenericParameters.Add(new GenericParameter(stringReference));
            constructor.GenericParameters.Add(new GenericParameter(objectReference));
            _dictionaryStringObjectConstructorReference = ModuleDefinition.ImportReference(constructor);
            */
            _dictionaryStringObjectReference = ModuleDefinition.ImportReference(dictionaryDefinition);
        }

        if (_jsonExtensionDataAttributeReference is null)
        {
            var jsonConstructorReference = ModuleDefinition.AssemblyResolver
                .Resolve(AssemblyNameReference.Parse("System.Text.Json")).MainModule
                .GetType("System.Text.Json.Serialization.JsonExtensionDataAttribute").Methods
                .First(m => m.IsConstructor && !m.HasParameters);
            _jsonExtensionDataAttributeReference = ModuleDefinition.ImportReference(jsonConstructorReference);
        }

        var propertyDefinition = new PropertyDefinition("ExtensionData", PropertyAttributes.None, _dictionaryStringObjectReference);
        propertyDefinition.CustomAttributes.Add(new CustomAttribute(_jsonExtensionDataAttributeReference));

        // Add backing field
        var field = new FieldDefinition("_extensionData",
            FieldAttributes.Private,
            _dictionaryStringObjectReference);
        typeDefinition.Fields.Add(field);

        // Add getter
        var get = new MethodDefinition("get_ExtensionData",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _dictionaryStringObjectReference);
        get.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        get.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, field));
        get.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        propertyDefinition.GetMethod = get;
        typeDefinition.Methods.Add(get);

        // Add setter
        var set = new MethodDefinition("set_ExtensionData",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            ModuleDefinition.TypeSystem.Void);
        set.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, _dictionaryStringObjectReference));

        set.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        set.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        set.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, field));
        set.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        propertyDefinition.SetMethod = set;
        typeDefinition.Methods.Add(set);
        typeDefinition.Properties.Add(propertyDefinition);
    }

    public static List<FieldDefinition> FieldsFlattened(TypeDefinition asmType)
    {
        //get properties on main type:
        var fields = new List<FieldDefinition>(asmType.Fields.Select(field => field));

        //get properties of base types:
        if (asmType.BaseType != null && asmType.BaseType.FullName != "System.Object")
        {
            var baseType = asmType.BaseType.Resolve();

            //recursive call:
            if (baseType != null)
                fields.AddRange(FieldsFlattened(baseType));
        }

        return fields;
    }
}
