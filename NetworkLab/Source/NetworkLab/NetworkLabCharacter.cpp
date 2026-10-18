// Copyright Epic Games, Inc. All Rights Reserved.

#include "NetworkLabCharacter.h"
#include "NetworkLabMovementComponent.h"
#include "Engine/LocalPlayer.h"
#include "Camera/CameraComponent.h"
#include "Components/CapsuleComponent.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/SpringArmComponent.h"
#include "GameFramework/Controller.h"
#include "EnhancedInputComponent.h"
#include "EnhancedInputSubsystems.h"
#include "InputActionValue.h"
#include "InputCoreTypes.h"
#include "Engine/Engine.h"
#include "GameFramework/PlayerController.h"
#include "TimerManager.h"
#include "NetworkLab.h"

ANetworkLabCharacter::ANetworkLabCharacter(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer.SetDefaultSubobjectClass<UNetworkLabMovementComponent>(ACharacter::CharacterMovementComponentName))
{
	// Set size for collision capsule
	GetCapsuleComponent()->InitCapsuleSize(42.f, 96.0f);
		
	// Don't rotate when the controller rotates. Let that just affect the camera.
	bUseControllerRotationPitch = false;
	bUseControllerRotationYaw = false;
	bUseControllerRotationRoll = false;

	// Configure character movement
	GetCharacterMovement()->bOrientRotationToMovement = true;
	GetCharacterMovement()->RotationRate = FRotator(0.0f, 500.0f, 0.0f);

	// Note: For faster iteration times these variables, and many more, can be tweaked in the Character Blueprint
	// instead of recompiling to adjust them
	GetCharacterMovement()->JumpZVelocity = 500.f;
	GetCharacterMovement()->AirControl = 0.35f;
	GetCharacterMovement()->MaxWalkSpeed = 500.f;
	GetCharacterMovement()->MinAnalogWalkSpeed = 20.f;
	GetCharacterMovement()->BrakingDecelerationWalking = 2000.f;
	GetCharacterMovement()->BrakingDecelerationFalling = 1500.0f;

	// Create a camera boom (pulls in towards the player if there is a collision)
	CameraBoom = CreateDefaultSubobject<USpringArmComponent>(TEXT("CameraBoom"));
	CameraBoom->SetupAttachment(RootComponent);
	CameraBoom->TargetArmLength = 400.0f;
	CameraBoom->bUsePawnControlRotation = true;

	// Create a follow camera
	FollowCamera = CreateDefaultSubobject<UCameraComponent>(TEXT("FollowCamera"));
	FollowCamera->SetupAttachment(CameraBoom, USpringArmComponent::SocketName);
	FollowCamera->bUsePawnControlRotation = false;

	// Note: The skeletal mesh and anim blueprint references on the Mesh component (inherited from Character) 
	// are set in the derived blueprint asset named ThirdPersonCharacter (to avoid direct content references in C++)
}

void ANetworkLabCharacter::SetupPlayerInputComponent(UInputComponent* PlayerInputComponent)
{
	// Set up action bindings
	if (UEnhancedInputComponent* EnhancedInputComponent = Cast<UEnhancedInputComponent>(PlayerInputComponent)) {
		PlayerInputComponent->BindKey(EKeys::LeftShift, IE_Pressed, this, &ANetworkLabCharacter::SprintPressed);
		PlayerInputComponent->BindKey(EKeys::LeftShift, IE_Released, this, &ANetworkLabCharacter::SprintReleased);
		PlayerInputComponent->BindKey(EKeys::F1, IE_Pressed, this, &ANetworkLabCharacter::ToggleSprintPrediction);
		PlayerInputComponent->BindKey(EKeys::F3, IE_Pressed, this, &ANetworkLabCharacter::InjectPredictionErrorForDemo);
		
		// Jumping
		EnhancedInputComponent->BindAction(JumpAction, ETriggerEvent::Started, this, &ACharacter::Jump);
		EnhancedInputComponent->BindAction(JumpAction, ETriggerEvent::Completed, this, &ACharacter::StopJumping);

		// Moving
		EnhancedInputComponent->BindAction(MoveAction, ETriggerEvent::Triggered, this, &ANetworkLabCharacter::Move);
		EnhancedInputComponent->BindAction(MouseLookAction, ETriggerEvent::Triggered, this, &ANetworkLabCharacter::Look);

		// Looking
		EnhancedInputComponent->BindAction(LookAction, ETriggerEvent::Triggered, this, &ANetworkLabCharacter::Look);
	}
	else
	{
		UE_LOG(LogNetworkLab, Error, TEXT("'%s' Failed to find an Enhanced Input component! This template is built to use the Enhanced Input system. If you intend to use the legacy system, then you will need to update this C++ file."), *GetNameSafe(this));
	}
}

void ANetworkLabCharacter::InjectPredictionErrorForDemo()
{
	if (!IsLocallyControlled() || HasAuthority())
	{
		if (GEngine && IsLocallyControlled())
		{
			GEngine->AddOnScreenDebugMessage(61286, 3.0f, FColor::Yellow, TEXT("Use F3 in the connected client window to demonstrate a correction."));
		}
		return;
	}

	if (!bUseClientSideSprintPrediction || !bSprintInputHeld)
	{
		if (GEngine)
		{
			GEngine->AddOnScreenDebugMessage(61286, 3.0f, FColor::Yellow, TEXT("Enable prediction with F1, then hold Shift and press F3."));
		}
		return;
	}

	if (UNetworkLabMovementComponent* Movement = Cast<UNetworkLabMovementComponent>(GetCharacterMovement()))
	{
		Movement->SetPredictionTestSpeedBoost(true);
		GetWorldTimerManager().SetTimer(PredictionErrorTimer, this, &ANetworkLabCharacter::RestorePredictionSpeedAfterDemo, 1.5f, false);
		UE_LOG(LogTemp, Display, TEXT("[Prediction] TEST ONLY: local sprint speed was temporarily raised; server should correct the owning client."));
		if (GEngine)
		{
			GEngine->AddOnScreenDebugMessage(61286, 3.0f, FColor::Red, TEXT("Test error injected: server correction expected"));
		}
	}
}

void ANetworkLabCharacter::RestorePredictionSpeedAfterDemo()
{
	if (UNetworkLabMovementComponent* Movement = Cast<UNetworkLabMovementComponent>(GetCharacterMovement()))
	{
		Movement->SetPredictionTestSpeedBoost(false);
	}
	UE_LOG(LogTemp, Display, TEXT("[Prediction] Temporary test speed has returned to the server's value."));
}

void ANetworkLabCharacter::SprintPressed()
{
	ApplySprintInput(true);
}

void ANetworkLabCharacter::SprintReleased()
{
	ApplySprintInput(false);
}

void ANetworkLabCharacter::ToggleSprintPrediction()
{
	if (!IsLocallyControlled())
	{
		return;
	}

	bUseClientSideSprintPrediction = !bUseClientSideSprintPrediction;
	if (UNetworkLabMovementComponent* Movement = Cast<UNetworkLabMovementComponent>(GetCharacterMovement()))
	{
		Movement->SetSprintIntent(false, bUseClientSideSprintPrediction);
	}
	ApplySprintInput(false);

	if (GEngine)
	{
		GEngine->AddOnScreenDebugMessage(static_cast<uint64>(GetUniqueID()), 3.0f, FColor::Cyan,
			bUseClientSideSprintPrediction
				? TEXT("Sprint: PREDICTION ON (F1 toggles; hold Shift while moving)")
				: TEXT("Sprint: SERVER RESPONSE (F1 toggles; hold Shift while moving)"));
	}
	UE_LOG(LogTemp, Display, TEXT("[Prediction] Local sprint mode changed: %s"), bUseClientSideSprintPrediction ? TEXT("predicted") : TEXT("server response"));
}

void ANetworkLabCharacter::ApplySprintInput(bool bWantsToSprint)
{
	if (!IsLocallyControlled())
	{
		return;
	}

	bSprintInputHeld = bWantsToSprint;
	LatestSprintRequestId = ++NextSprintRequestId;

	if (UNetworkLabMovementComponent* Movement = Cast<UNetworkLabMovementComponent>(GetCharacterMovement()))
	{
		if (HasAuthority() || bUseClientSideSprintPrediction || !bWantsToSprint)
		{
			Movement->SetSprintIntent(bWantsToSprint, bUseClientSideSprintPrediction);
		}
	}

	if (!HasAuthority())
	{
		ServerRequestSprint(bWantsToSprint, bUseClientSideSprintPrediction, LatestSprintRequestId);
	}
}

void ANetworkLabCharacter::ServerRequestSprint_Implementation(bool bWantsToSprint, bool bUsedPrediction, uint16 RequestId)
{
	UNetworkLabMovementComponent* Movement = Cast<UNetworkLabMovementComponent>(GetCharacterMovement());
	const bool bApproved = !bWantsToSprint || (Movement && Movement->IsMovingOnGround());
	if (Movement && !bUsedPrediction)
	{
		Movement->SetServerSprintAuthorization(bWantsToSprint && bApproved);
	}
	ClientReceiveSprintApproval(bApproved, RequestId);
	UE_LOG(LogTemp, Display, TEXT("[Server] Sprint request %u: %s"), RequestId, bApproved ? TEXT("approved") : TEXT("rejected; character is airborne"));
}

void ANetworkLabCharacter::ClientReceiveSprintApproval_Implementation(bool bApproved, uint16 RequestId)
{
	if (!IsLocallyControlled() || RequestId != LatestSprintRequestId)
	{
		return;
	}

	if (!bUseClientSideSprintPrediction && bSprintInputHeld && bApproved)
	{
		if (UNetworkLabMovementComponent* Movement = Cast<UNetworkLabMovementComponent>(GetCharacterMovement()))
		{
			Movement->SetSprintIntent(true, false);
		}
	}
	else if (!bApproved)
	{
		if (UNetworkLabMovementComponent* Movement = Cast<UNetworkLabMovementComponent>(GetCharacterMovement()))
		{
			Movement->SetSprintIntent(false, bUseClientSideSprintPrediction);
		}
	}

	UE_LOG(LogTemp, Display, TEXT("[Client] Sprint request %u: %s"), RequestId, bApproved ? TEXT("approved") : TEXT("rejected by server"));
}

void ANetworkLabCharacter::Move(const FInputActionValue& Value)
{
	// input is a Vector2D
	FVector2D MovementVector = Value.Get<FVector2D>();

	// route the input
	DoMove(MovementVector.X, MovementVector.Y);
}

void ANetworkLabCharacter::Look(const FInputActionValue& Value)
{
	// input is a Vector2D
	FVector2D LookAxisVector = Value.Get<FVector2D>();

	// route the input
	DoLook(LookAxisVector.X, LookAxisVector.Y);
}

void ANetworkLabCharacter::DoMove(float Right, float Forward)
{
	if (GetController() != nullptr)
	{
		// find out which way is forward
		const FRotator Rotation = GetController()->GetControlRotation();
		const FRotator YawRotation(0, Rotation.Yaw, 0);

		// get forward vector
		const FVector ForwardDirection = FRotationMatrix(YawRotation).GetUnitAxis(EAxis::X);

		// get right vector 
		const FVector RightDirection = FRotationMatrix(YawRotation).GetUnitAxis(EAxis::Y);

		// add movement 
		AddMovementInput(ForwardDirection, Forward);
		AddMovementInput(RightDirection, Right);
	}
}

void ANetworkLabCharacter::DoLook(float Yaw, float Pitch)
{
	if (GetController() != nullptr)
	{
		// add yaw and pitch input to controller
		AddControllerYawInput(Yaw);
		AddControllerPitchInput(Pitch);
	}
}

void ANetworkLabCharacter::DoJumpStart()
{
	// signal the character to jump
	Jump();
}

void ANetworkLabCharacter::DoJumpEnd()
{
	// signal the character to stop jumping
	StopJumping();
}
