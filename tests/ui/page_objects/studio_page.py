"""
Studio (Voice Synthesis) Page Object.

Provides methods for interacting with the main voice synthesis panel.
"""

from __future__ import annotations

import time
from typing import TYPE_CHECKING

from .base_page import BasePage

if TYPE_CHECKING:
    pass


class StudioPage(BasePage):
    """Page object for the Voice Synthesis (Studio) panel."""

    # =========================================================================
    # Automation IDs
    # =========================================================================

    @property
    def root_automation_id(self) -> str:
        return "VoiceSynthesisView_Root"

    @property
    def nav_automation_id(self) -> str:
        return "NavStudio"

    # Element IDs
    PROFILE_COMBO = "VoiceSynthesisView_ProfileComboBox"
    ENGINE_COMBO = "VoiceSynthesisView_EngineComboBox"
    LANGUAGE_COMBO = "VoiceSynthesisView_LanguageComboBox"
    EMOTION_COMBO = "VoiceSynthesisView_EmotionComboBox"
    TEXT_INPUT = "VoiceSynthesisView_TextInput"
    SYNTHESIZE_BUTTON = "VoiceSynthesisView_SynthesizeButton"
    STOP_BUTTON = "VoiceSynthesisView_StopButton"
    PLAY_BUTTON = "VoiceSynthesisView_PlayButton"
    REFRESH_BUTTON = "VoiceSynthesisView_RefreshButton"
    OUTPUT_PATH_TEXT = "VoiceSynthesisView_OutputPathText"
    SPEED_SLIDER = "VoiceSynthesisView_SpeedSlider"
    PITCH_SLIDER = "VoiceSynthesisView_PitchSlider"

    # =========================================================================
    # Properties
    # =========================================================================

    @property
    def text_input(self) -> str | None:
        """Get the current text input value."""
        return self.get_text(self.TEXT_INPUT)

    @property
    def output_path(self) -> str | None:
        """Get the current output path."""
        return self.get_text(self.OUTPUT_PATH_TEXT)

    @property
    def is_synthesize_enabled(self) -> bool:
        """Check if the synthesize button is enabled."""
        try:
            element = self.find_element(self.SYNTHESIZE_BUTTON, timeout=2)
            return element.is_enabled()
        except RuntimeError:
            return False

    # =========================================================================
    # Actions
    # =========================================================================

    def enter_text(self, text: str) -> bool:
        """
        Enter text to synthesize.

        Args:
            text: The text to synthesize.

        Returns:
            True if successful.
        """
        return self.type_text(self.TEXT_INPUT, text)

    def select_profile(self, profile_name: str) -> bool:
        """
        Select a voice profile.

        Args:
            profile_name: Name of the profile to select.

        Returns:
            True if successful.
        """
        return self.select_combobox_item(self.PROFILE_COMBO, profile_name)

    def select_engine(self, engine_name: str) -> bool:
        """
        Select a synthesis engine.

        Args:
            engine_name: Name of the engine to select.

        Returns:
            True if successful.
        """
        return self.select_combobox_item(self.ENGINE_COMBO, engine_name)

    def select_language(self, language: str) -> bool:
        """
        Select a language.

        Args:
            language: Language to select.

        Returns:
            True if successful.
        """
        return self.select_combobox_item(self.LANGUAGE_COMBO, language)

    def select_emotion(self, emotion: str) -> bool:
        """
        Select an emotion/style.

        Args:
            emotion: Emotion to select.

        Returns:
            True if successful.
        """
        return self.select_combobox_item(self.EMOTION_COMBO, emotion)

    def click_synthesize(self) -> bool:
        """
        Click the synthesize button.

        Returns:
            True if successful.
        """
        return self.click_with_retry(self.SYNTHESIZE_BUTTON)

    def click_stop(self) -> bool:
        """
        Click the stop button.

        Returns:
            True if successful.
        """
        return self.click_with_retry(self.STOP_BUTTON)

    def click_play(self) -> bool:
        """
        Click the play button.

        Returns:
            True if successful.
        """
        return self.click_with_retry(self.PLAY_BUTTON)

    def refresh_profiles(self) -> bool:
        """
        Click the refresh button.

        Returns:
            True if successful.
        """
        return self.click_with_retry(self.REFRESH_BUTTON)

    # =========================================================================
    # Workflows
    # =========================================================================

    def synthesize_text(
        self, text: str, wait_for_completion: bool = True, timeout: float = 30.0
    ) -> bool:
        """
        Complete workflow to synthesize text.

        Args:
            text: Text to synthesize.
            wait_for_completion: Whether to wait for synthesis to complete.
            timeout: Maximum time to wait for completion.

        Returns:
            True if synthesis started (and completed if wait_for_completion).
        """
        # Enter text
        if not self.enter_text(text):
            return False

        # Wait for synthesize button to be enabled
        time.sleep(0.3)

        # Click synthesize
        if not self.click_synthesize():
            return False

        if wait_for_completion:
            # Wait for play button to become visible (indicates completion)
            start = time.time()
            while time.time() - start < timeout:
                if self.element_exists(self.PLAY_BUTTON):
                    try:
                        play = self.find_element(self.PLAY_BUTTON, timeout=1)
                        if play.is_enabled():
                            return True
                    # ALLOWED: bare except - best effort, failure acceptable
                    except RuntimeError:
                        pass
                time.sleep(0.5)
            return False

        return True

    def verify_elements_present(self) -> dict:
        """
        Verify all critical elements are present.

        Returns:
            Dictionary with element names and their presence status.
        """
        elements = {
            "root": self.root_automation_id,
            "profile_combo": self.PROFILE_COMBO,
            "engine_combo": self.ENGINE_COMBO,
            "text_input": self.TEXT_INPUT,
            "synthesize_button": self.SYNTHESIZE_BUTTON,
        }

        return {name: self.element_exists(auto_id) for name, auto_id in elements.items()}
