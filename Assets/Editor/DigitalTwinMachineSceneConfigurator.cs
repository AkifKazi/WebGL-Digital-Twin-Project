using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public interface IDigitalTwinMachineSceneConfigurator
{
    MonoBehaviour Configure(
        GameObject runtimeRoot,
        TelemetryRegistry registry);
}

public interface IDigitalTwinMachineConfigurationProvider
{
    bool IsDefault { get; }
    DigitalTwinMachineConfiguration CreateConfiguration();
}

public static class DigitalTwinMachineConfigurationLoader
{
    public static DigitalTwinMachineConfiguration LoadDefault()
    {
        DigitalTwinMachineConfiguration[] configurations = AssetDatabase
            .FindAssets("t:DigitalTwinMachineConfiguration")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<DigitalTwinMachineConfiguration>)
            .Where(configuration => configuration != null)
            .ToArray();

        if (configurations.Length == 0)
            return LoadDefaultProviderConfiguration();

        DigitalTwinMachineConfiguration[] defaults = configurations
            .Where(configuration => configuration.isDefault)
            .ToArray();

        if (defaults.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one default machine configuration, found {defaults.Length}.");
        }

        return defaults[0];
    }

    private static DigitalTwinMachineConfiguration LoadDefaultProviderConfiguration()
    {
        IDigitalTwinMachineConfigurationProvider[] providers = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(assembly =>
            {
                try { return assembly.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .Where(type => !type.IsAbstract &&
                           typeof(IDigitalTwinMachineConfigurationProvider).IsAssignableFrom(type))
            .Select(type => (IDigitalTwinMachineConfigurationProvider)Activator.CreateInstance(type))
            .Where(provider => provider.IsDefault)
            .ToArray();

        if (providers.Length != 1)
        {
            throw new InvalidOperationException(
                $"No configuration asset exists and expected exactly one default machine " +
                $"configuration provider, found {providers.Length}.");
        }

        return providers[0].CreateConfiguration();
    }

    public static IDigitalTwinMachineSceneConfigurator CreateSceneConfigurator(
        DigitalTwinMachineConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.editorConfiguratorType))
            return null;

        Type configuratorType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(configuration.editorConfiguratorType, false))
            .FirstOrDefault(type => type != null);

        if (configuratorType == null ||
            !typeof(IDigitalTwinMachineSceneConfigurator).IsAssignableFrom(configuratorType))
        {
            throw new InvalidOperationException(
                $"Machine '{configuration.machineId}' references invalid scene configurator " +
                $"'{configuration.editorConfiguratorType}'.");
        }

        return (IDigitalTwinMachineSceneConfigurator)Activator.CreateInstance(configuratorType);
    }
}

public sealed class VibratoryFeederSceneConfigurator : IDigitalTwinMachineSceneConfigurator
{
    public MonoBehaviour Configure(
        GameObject runtimeRoot,
        TelemetryRegistry registry)
    {
        foreach (HopperProcessSimulator legacy in UnityEngine.Object
                     .FindObjectsByType<HopperProcessSimulator>(FindObjectsInactive.Include)
                     .Where(component => component.gameObject != runtimeRoot))
        {
            UnityEngine.Object.DestroyImmediate(legacy);
        }
        foreach (ParticleFlowController legacy in UnityEngine.Object
                     .FindObjectsByType<ParticleFlowController>(FindObjectsInactive.Include)
                     .Where(component => component.gameObject != runtimeRoot))
        {
            UnityEngine.Object.DestroyImmediate(legacy);
        }

        ParticleFlowController particleController =
            runtimeRoot.GetComponent<ParticleFlowController>();
        if (particleController == null)
            particleController = runtimeRoot.AddComponent<ParticleFlowController>();

        SerializedObject particleData = new(particleController);
        particleData.FindProperty("flowSpeed").floatValue = 1f;
        particleData.FindProperty("quantity").floatValue = 1f;
        particleData.FindProperty("discoverInChildren").boolValue = false;
        SetObjectArray(
            particleData.FindProperty("particleSystems"),
            UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include));
        SetObjectArray(
            particleData.FindProperty("rockFlows"),
            UnityEngine.Object.FindObjectsByType<ControlledRockFlow>(FindObjectsInactive.Include));
        SetObjectArray(
            particleData.FindProperty("filteredFlows"),
            UnityEngine.Object.FindObjectsByType<FilteredParticleFlow>(FindObjectsInactive.Include));
        particleData.ApplyModifiedPropertiesWithoutUndo();

        HopperProcessSimulator simulator = runtimeRoot.GetComponent<HopperProcessSimulator>();
        if (simulator == null)
            simulator = runtimeRoot.AddComponent<HopperProcessSimulator>();

        SerializedObject simulatorData = new(simulator);
        simulatorData.FindProperty("registry").objectReferenceValue = registry;
        simulatorData.FindProperty("particleFlowController").objectReferenceValue = particleController;
        simulatorData.FindProperty("driveParticleControlsFromProcess").boolValue = false;
        simulatorData.ApplyModifiedPropertiesWithoutUndo();
        return simulator;
    }

    private static void SetObjectArray<T>(SerializedProperty property, T[] values)
        where T : UnityEngine.Object
    {
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
