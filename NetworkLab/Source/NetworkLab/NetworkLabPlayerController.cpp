// Copyright Epic Games, Inc. All Rights Reserved.


#include "NetworkLabPlayerController.h"
#include "EnhancedInputSubsystems.h"
#include "Engine/LocalPlayer.h"
#include "InputMappingContext.h"
#include "Blueprint/UserWidget.h"
#include "NetworkLab.h"
#include "Widgets/Input/SVirtualJoystick.h"
#include "InputCoreTypes.h"
#include "NetworkLabGameMode.h"
#include "Engine/Engine.h"

void ANetworkLabPlayerController::BeginPlay()
{
	Super::BeginPlay();

	// only spawn touch controls on local player controllers
	if (IsLocalPlayerController() && ShouldUseTouchControls())
	{
		// spawn the mobile controls widget
		MobileControlsWidget = CreateWidget<UUserWidget>(this, MobileControlsWidgetClass);

		if (MobileControlsWidget)
		{
			// add the controls to the player screen
			MobileControlsWidget->AddToPlayerScreen(0);

		} else {

			UE_LOG(LogNetworkLab, Error, TEXT("Could not spawn mobile controls widget."));

		}

	}
}

void ANetworkLabPlayerController::SetupInputComponent()
{
	Super::SetupInputComponent();
	InputComponent->BindKey(EKeys::F2, IE_Pressed, this, &ANetworkLabPlayerController::ToggleReplicationOptimization);

	// only add IMCs for local player controllers
	if (IsLocalPlayerController())
	{
		// Add Input Mapping Contexts
		if (UEnhancedInputLocalPlayerSubsystem* Subsystem = ULocalPlayer::GetSubsystem<UEnhancedInputLocalPlayerSubsystem>(GetLocalPlayer()))
		{
			for (UInputMappingContext* CurrentContext : DefaultMappingContexts)
			{
				Subsystem->AddMappingContext(CurrentContext, 0);
			}

			// only add these IMCs if we're not using mobile touch input
			if (!ShouldUseTouchControls())
			{
				for (UInputMappingContext* CurrentContext : MobileExcludedMappingContexts)
				{
					Subsystem->AddMappingContext(CurrentContext, 0);
				}
			}
		}
	}
}

void ANetworkLabPlayerController::ToggleReplicationOptimization()
{
	if (!HasAuthority())
	{
		if (GEngine && IsLocalPlayerController())
		{
			GEngine->AddOnScreenDebugMessage(61285, 3.0f, FColor::Yellow, TEXT("Use F2 in the listen-server window to change the replication profile."));
		}
		return;
	}

	if (ANetworkLabGameMode* GameMode = GetWorld() ? GetWorld()->GetAuthGameMode<ANetworkLabGameMode>() : nullptr)
	{
		GameMode->ToggleReplicationOptimization();
	}
}

bool ANetworkLabPlayerController::ShouldUseTouchControls() const
{
	// are we on a mobile platform? Should we force touch?
	return SVirtualJoystick::ShouldDisplayTouchInterface() || bForceTouchControls;
}
