@tool
extends EditorPlugin

class QuestManifestExport extends EditorExportPlugin:
	func _get_name() -> String:
		return "MechRewiredQuestManifest"

	func _supports_platform(platform: EditorExportPlatform) -> bool:
		return platform is EditorExportPlatformAndroid

	func _get_android_manifest_application_element_contents(_platform: EditorExportPlatform, _debug: bool) -> String:
		return '<meta-data android:name="com.oculus.supportedDevices" android:value="quest3|quest3s" />\n<meta-data android:name="com.oculus.vr.focusaware" android:value="true" />'

	func _get_android_manifest_activity_element_contents(_platform: EditorExportPlatform, _debug: bool) -> String:
		return '<meta-data android:name="com.oculus.vr.focusaware" android:value="true" />\n<intent-filter><action android:name="android.intent.action.MAIN" /><category android:name="com.oculus.intent.category.VR" /></intent-filter>'

var quest_export: EditorExportPlugin

func _enter_tree() -> void:
	quest_export = QuestManifestExport.new()
	add_export_plugin(quest_export)

func _exit_tree() -> void:
	remove_export_plugin(quest_export)
