// Copyright Epic Games, Inc. All Rights Reserved.

using UnrealBuildTool;

public class NetworkLab : ModuleRules
{
	public NetworkLab(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] {
			"Core",
			"CoreUObject",
			"Engine",
			"InputCore",
			"EnhancedInput",
			"AIModule",
			"StateTreeModule",
			"GameplayStateTreeModule",
			"UMG",
			"Slate"
		});

		PrivateDependencyModuleNames.AddRange(new string[] { });

		PublicIncludePaths.AddRange(new string[] {
			"NetworkLab",
			"NetworkLab/Variant_Platforming",
			"NetworkLab/Variant_Platforming/Animation",
			"NetworkLab/Variant_Combat",
			"NetworkLab/Variant_Combat/AI",
			"NetworkLab/Variant_Combat/Animation",
			"NetworkLab/Variant_Combat/Gameplay",
			"NetworkLab/Variant_Combat/Interfaces",
			"NetworkLab/Variant_Combat/UI",
			"NetworkLab/Variant_SideScrolling",
			"NetworkLab/Variant_SideScrolling/AI",
			"NetworkLab/Variant_SideScrolling/Gameplay",
			"NetworkLab/Variant_SideScrolling/Interfaces",
			"NetworkLab/Variant_SideScrolling/UI"
		});

		// Uncomment if you are using Slate UI
		// PrivateDependencyModuleNames.AddRange(new string[] { "Slate", "SlateCore" });

		// Uncomment if you are using online features
		// PrivateDependencyModuleNames.Add("OnlineSubsystem");

		// To include OnlineSubsystemSteam, add it to the plugins section in your uproject file with the Enabled attribute set to true
	}
}
