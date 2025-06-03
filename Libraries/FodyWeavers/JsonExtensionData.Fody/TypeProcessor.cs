using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;

namespace JsonExtensionData.Fody;

public partial class ModuleWeaver
{
    private TypeReference? _dictionaryStringObjectReference;
    private MethodReference? _jsonExtensionDataAttributeReference;
    private MethodReference? _jsonIgnoreAttributeReference;
    public void ProcessType(TypeDefinition typeDefinition)
    {
        if (typeDefinition.BaseType != null && typeDefinition.BaseType.FullName != "System.Object")
        {
            return;
        }

        // If the property already has somewhere on the inheritance chain the JsonExtensionDataAttribute, dont add it again
        if (PropertiesFlattened(typeDefinition).Any(p => p.CustomAttributes.Any(a => a.AttributeType.FullName.Contains("JsonExtensionDataAttribute"))))
        {
            return;
        }

        _dictionaryStringObjectReference ??= ModuleDefinition.ImportReference(typeof(Dictionary<string, object>));
        if (_jsonExtensionDataAttributeReference is null)
        {
            var jsonConstructorReference = ModuleDefinition.AssemblyResolver
                .Resolve(AssemblyNameReference.Parse("System.Text.Json")).MainModule
                .GetType("System.Text.Json.Serialization.JsonExtensionDataAttribute").Methods
                .First(m => m.IsConstructor && !m.HasParameters);
            _jsonExtensionDataAttributeReference = ModuleDefinition.ImportReference(jsonConstructorReference);
        }
        if (_jsonIgnoreAttributeReference is null)
        {
            var jsonIgnoreConstructorReference = ModuleDefinition.AssemblyResolver
                .Resolve(AssemblyNameReference.Parse("System.Text.Json")).MainModule
                .GetType("System.Text.Json.Serialization.JsonIgnoreAttribute").Methods
                .First(m => m.IsConstructor && !m.HasParameters);
            _jsonIgnoreAttributeReference = ModuleDefinition.ImportReference(jsonIgnoreConstructorReference);
        }
        var propertyDefinition = new PropertyDefinition("ExtensionData", PropertyAttributes.None, _dictionaryStringObjectReference);
        propertyDefinition.CustomAttributes.Add(new CustomAttribute(_jsonExtensionDataAttributeReference));

        // Add backing field
        var field = new FieldDefinition("_extensionData",
            FieldAttributes.Private,
            _dictionaryStringObjectReference);
        field.CustomAttributes.Add(new CustomAttribute(_jsonIgnoreAttributeReference));
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

        foreach (var constructor in typeDefinition.Methods.Where(m => m.IsConstructor && !m.IsStatic))
        {
            var processor = constructor.Body.GetILProcessor();
            var ldArg0 = Instruction.Create(OpCodes.Ldarg_0);
            processor.InsertBefore(constructor.Body.Instructions.First(), ldArg0);
            var newInstruction = processor.Create(OpCodes.Newobj, ModuleDefinition.ImportReference(typeof(Dictionary<string, object>).GetConstructor(Type.EmptyTypes)));
            processor.InsertAfter(ldArg0, newInstruction);
            var setField = processor.Create(OpCodes.Stfld, field);
            processor.InsertAfter(newInstruction, setField);
        }

        propertyDefinition.SetMethod = set;
        typeDefinition.Methods.Add(set);
        typeDefinition.Properties.Add(propertyDefinition);
    }

    public static List<PropertyDefinition> PropertiesFlattened(TypeDefinition asmType)
    {
        //get properties on main type:
        var props = new List<PropertyDefinition>(asmType.Properties.Select(property => property));

        //get properties of base types:
        if (asmType.BaseType != null && asmType.BaseType.FullName != "System.Object")
        {
            var baseType = asmType.BaseType.Resolve();

            //recursive call:
            if (baseType != null)
                props.AddRange(PropertiesFlattened(baseType));
        }

        return props;
    }
}
