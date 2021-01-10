using System.Collections;
using System.Linq;
using System;
using KModkit;
using UnityEngine;
using Rand = UnityEngine.Random;

public class WarGamesModuleScript : MonoBehaviour {

	//basic components
	public KMBombInfo Bomb;
	public KMBombModule Module;
	public KMAudio Audio;
	public bool[] activeDigits = new bool[14] { true, true, true, true, true, true, true, true, true, true, true, true, true, true };
	public TextMesh[] Digits;
	public TextMesh[] ConfirmDigits;
	public Light waitingLight;
	public Light busyLight;
	public enum Status
    {
		Start,
		Busy,
		Waiting,
		Input,
		Solved
    }

	//sfx
	public AudioClip[] MouseTrapSounds;
	public AudioClip[] MouseTrapStarts;
	public AudioClip[] GreyGooseSounds;
	public AudioClip[] GreyGooseStarts;
	public AudioClip[] BlackHoleSounds;
	public AudioClip[] BlackHoleStarts;
	public AudioClip[] ModuleSounds;

	//buttons
	public KMSelectable ReceiveButton;
	public KMSelectable SendButton;
	public KMSelectable[] DigitArrows;
	public KMSelectable[] ConfirmationArrows;

	//logging
	private static int moduleIdCounter = 1;
	private int moduleID;
	private readonly string[] Ciphers = new string[3] { "Post-Modern", "Rot18", "MAtbash"};

	//logic
	private enum ResponseType
    {
		Jamming,
		Error,
		First,
		Second, 
		Either
    }
	private enum MessageColor
    {
		Red, 
		Yellow, 
		Green
    }
	private readonly string Numbers = "0123456789";
	private readonly string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
	private readonly string AlphabetandNumbers = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
	private readonly string PostModernAlphabet = "1234567890QWERTYUIOPASDFGHJKLZXCVBNM";
    private readonly string GoodLetters = "YESGOODYES7777";
	private readonly string BadLetters = "BADNOPEBAD6666";
#pragma warning disable IDE0044 
	private bool[] correctParts = new bool[3] { false, false, false }; //in order, they are 1st part, 2nd part, authenication
	private string[] outMessages = new string[4] { "", "", "", "" }; //in order they are 1st part end, dec, 2nd part end, dec
#pragma warning restore IDE0044
	private string siloID = ""; 
	private int outAuthCode;
	private int ansAuthCode;
	private ResponseType correctResponse;
	private MessageColor correctColor;
	private Status mStatus = Status.Busy;
	bool TimeModeActive;
	bool ZenModeActive;

	//TP
	bool tpAutosolve = false;


	// Use this for initialization
	private void Start () 
	{
		moduleID = moduleIdCounter++;
		foreach (KMSelectable Arrow in DigitArrows)
        {
			Arrow.OnInteract += delegate () { ArrowPress(Arrow); return false; };
        }
		foreach (KMSelectable Arrow in ConfirmationArrows)
        {
			Arrow.OnInteract += delegate () { ConfirmArrowPress(Arrow); return false; };
        }
		ReceiveButton.OnInteract += delegate () { BeginModule(); return false; };
		SendButton.OnInteract += delegate () { SubmitModule(); return false; };
		Module.OnActivate += Activate;

		waitingLight.range *= transform.lossyScale.x;
		busyLight.range *= transform.lossyScale.x;

		StartCoroutine(BusyLightRoutine());
		StartCoroutine(WaitingLightRoutine());
		StartCoroutine(RotateLetters());
	}
	
	// Update is called once per frame
	void Update () {
	}

	private void Activate()
    {
		CalculateConditions();
		mStatus = Status.Start;
		Audio.PlaySoundAtTransform(ModuleSounds[0].name, transform);
	}

	void Log(string message)
    {
		Debug.Log("[Silo Authorization #" + moduleID + "] " + message);
    }

	void DebugLog(string message)
	{
		Debug.Log("<Silo Authorization #" + moduleID + "> " + message);
	}

	void ArrowPress(KMSelectable Arrow)
    {
		if (mStatus != Status.Input) return;
		int arrowIndex = Array.IndexOf(DigitArrows, Arrow);
		int arrowPos = arrowIndex % 2;
		int arrowPlace = ((arrowPos == 1) ? arrowIndex - 1 : arrowIndex) / 2;

		activeDigits[arrowPlace] = false;
		Arrow.AddInteractionPunch(0.2f);
		Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, transform);
		Digits[arrowPlace].text = ToChar(Digits[arrowPlace].text, (arrowPos == 0 ? 1 : -1));
    }

	void ConfirmArrowPress(KMSelectable Arrow)
    {
		if (mStatus != Status.Input) return;
		int arrowIndex = Array.IndexOf(ConfirmationArrows, Arrow);
		int arrowPos = arrowIndex % 2;
		int arrowPlace = ((arrowPos == 1) ? arrowIndex - 1 : arrowIndex) / 2;

		activeDigits[arrowPlace + 10] = false;
		Arrow.AddInteractionPunch(0.2f);
		Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, transform);
		int toDisplay;
		if (!int.TryParse(ConfirmDigits[arrowPlace].text, out toDisplay))
			DebugLog("fuck");
		if (arrowPos == 0)
        {
			toDisplay++;
			if (toDisplay > 9)
				toDisplay = 0;
		}
        else
        {
			toDisplay--;
			if (toDisplay < 0)
				toDisplay = 9;
		}
		ConfirmDigits[arrowPlace].text = toDisplay.ToString();
	}

	void BeginModule()
    {
		if (mStatus != Status.Start && mStatus != Status.Input) return;
		ReceiveButton.AddInteractionPunch(0.2f);
		Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, transform);
		StartCoroutine(AudioHandler(mStatus == Status.Input || tpAutosolve));
    }

	void SubmitModule() 
	{
		if (mStatus != Status.Input)
		{
			DebugLog(CalculateSolution());
			return;
		}
		mStatus = Status.Busy;
		SendButton.AddInteractionPunch(0.2f);
		Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, transform);
		Log(VerifySolution(true) ? "Correct, module solved." : "Incorrect, strike."); 
	}

	string ToChar(string input, int shift)
    {
		int num;
		if (!int.TryParse(input, out num))
			num = Array.IndexOf(Alphabet.ToCharArray(), input[0]) + 10;
		num += shift;
		while (num > 35 || num < 0)
        {
			if (num < 0)
				num += 36;
			else if (num > 35)
				num -= 36;
        }
		if (num < 10)
			return num.ToString();
		else
			return Alphabet[num - 10].ToString();
    }

	int ToNum (string input, int shift)
    {
		int num;
		if (!int.TryParse(input, out num))
			num = Array.IndexOf(Alphabet.ToCharArray(), input[0]) + 10;
		num += shift;
		while (num > 35 || num < 0)
		{
			if (num < 0)
				num += 36;
			else if (num > 35)
				num -= 36;
		}
		return num;
	}

	void CalculateConditions()
    {
		outAuthCode = 0;
		for (int i = 0; i < 4; i++)
			ConfirmDigits[i].text = "0";
		for (int i = 0; i < 10; i++)
			Digits[i].text = "0";

		//siloID
		siloID = "";
		string siloBuild = ToChar((Bomb.GetBatteryHolderCount() % 36).ToString(), 0) + ToChar((Bomb.GetBatteryCount() % 36).ToString(), 0) + ToChar((Bomb.GetPortPlateCount() % 36).ToString(), 0);
		for (int i = 0; i < 3; i++)
        {
			int result = -1;
			if (int.TryParse(siloBuild[i].ToString(), out result))
            {
				if (result == 0)
					siloID += Bomb.GetSerialNumberLetters().First();
				else if (result > 0)
					siloID += siloBuild[i];
			}
			else siloID += siloBuild[i];
		}

		//message calculation
		int wrongIndex = Rand.Range(0, 10);
		outMessages[1] = ToChar(Rand.Range(10, 36).ToString(), 0) + (Bomb.GetIndicators().ToArray().Count() < 1 ? "MRP" : Bomb.GetIndicators().ToArray()[Rand.Range(0, Bomb.GetIndicators().ToArray().Count())]);
		if (wrongIndex == 4 || wrongIndex == 5 || wrongIndex == 8 || wrongIndex == 9)
			for (int i = 0; i < Rand.Range(1, 3); i++)
            {
				int index2 = Rand.Range(1, 4);
				int index3 = Rand.Range(0, 26);
				if (index2 == 1)
					outMessages[1] = "" + outMessages[1][0] + Alphabet[index3] + outMessages[1][2] + outMessages[1][3];
				else if (index2 == 2)
					outMessages[1] = "" + outMessages[1][0] + outMessages[1][1] + Alphabet[index3] + outMessages[1][3];
				else
					outMessages[1] = "" + outMessages[1][0] + outMessages[1][1] + outMessages[1][2] + Alphabet[index3];
			}
		outMessages[0] = outMessages[1][0].ToString() + Encryptor(outMessages[1][1].ToString() + outMessages[1][2].ToString() + outMessages[1][3].ToString(), outMessages[1][0].ToString(), true);
		outMessages[3] = ToChar(Rand.Range(10, 36).ToString(), 0) + Bomb.GetSerialNumber()[0] + Bomb.GetSerialNumber()[2] + Bomb.GetSerialNumber()[5];
		if (wrongIndex > 5)
			for (int i = 0; i < Rand.Range(1, 3); i++)
			{
				int index2 = Rand.Range(1, 4);
				int index3 = Rand.Range(0, 26);
				if (index2 == 1)
					outMessages[3] = "" + outMessages[3][0] + Alphabet[index3] + outMessages[3][2] + outMessages[3][3];
				else if (index2 == 2)
					outMessages[3] = "" + outMessages[3][0] + outMessages[3][1] + Alphabet[index3] + outMessages[3][3];
				else
					outMessages[3] = "" + outMessages[3][0] + outMessages[3][1] + outMessages[3][2] + Alphabet[index3];
			}
		outMessages[2] = outMessages[3][0].ToString() + Encryptor(outMessages[3][1].ToString() + outMessages[3][2].ToString() + outMessages[3][3].ToString(), outMessages[3][0].ToString(), true);
		outMessages[2] = outMessages[3][0].ToString() + Encryptor(outMessages[3][1].ToString() + outMessages[3][2].ToString() + outMessages[3][3].ToString(), outMessages[3][0].ToString(), true);
		Log("The first part of the message is " + outMessages[1] + ", which is encrypted as " + outMessages[0] + ".");
		Log("The second part of the message is " + outMessages[3] + ", which is encrypted as " + outMessages[2] + ".");

		//authentication
		outAuthCode = 0;
		int[] logAuth = new int[6] { 0, 0, 0, 0, 0, 0 };
		for (int i = 0; i < 2; i++)
        {
			for (int j = 0; j < 3; j++)
            {
				logAuth[j + (3 * i)] += (ToNum(outMessages[1 + (2 * i)][j + 1].ToString(), 0) * ToNum(Bomb.GetSerialNumber()[j + (3 * i)].ToString(), 0));
				outAuthCode += logAuth[j + (3 * i)];
			}
        }
		string build = "The Authentication numbers are ";
		for (int i = 0; i < 6; i++)
			build += logAuth[i].ToString() + (i == 4 ? " and " : ", ");
		Log(build + "which totals up to " + (outAuthCode % 10000).ToString("0000") + ".");
		if (Rand.Range(0, 3) == 2)
		{
			outAuthCode = (outAuthCode + Rand.Range(1, 10000)) % 10000;
			correctParts[2] = false;
		}
		else
			correctParts[2] = true;
		Log("You are given " + outAuthCode.ToString("0000") + ", which is " + (correctParts[2] ? "valid." : "invalid."));

		//color / type
		int colorIndex = Rand.Range(0, 5);
		correctColor = colorIndex == 0 ? MessageColor.Red : colorIndex < 3 ? MessageColor.Yellow : MessageColor.Green;
		Log("Your message type is " + ((correctColor == MessageColor.Red) ? "Red-Alpha" : correctColor == MessageColor.Yellow ? "Yellow-Alpha" : "Green-Alpha") + ".");

		//message checking
		string[] checkParts = new string[2] { Encryptor(outMessages[0][1].ToString() + outMessages[0][2].ToString() + outMessages[0][3].ToString(), outMessages[0][0].ToString(), false) , Encryptor(outMessages[2][1].ToString() + outMessages[2][2].ToString() + outMessages[2][3].ToString(), outMessages[2][0].ToString(), false) };
		correctParts[0] = Bomb.GetIndicators().Count() == 0 ? checkParts[0] == "MRP" : Bomb.GetIndicators().Any(str => str == checkParts[0]);
		correctParts[1] = Bomb.GetSerialNumber().Contains(checkParts[1][0]) && Bomb.GetSerialNumber().Contains(checkParts[1][1]) && Bomb.GetSerialNumber().Contains(checkParts[1][2]);
		Log("The first part of the message is " + (correctParts[0] ? "valid" : "invalid") + ", and the second part of the message is " + (correctParts[1] ? "valid." : "invalid."));
		Log("The Silo ID is " + siloID + ".");
		if (!correctParts[2])
		{
			correctResponse = ResponseType.Jamming;
			Log("The Authentication code is invalid, you must respond with a Jamming message.");
		}
		else if (!correctParts[0] && !correctParts[1])
		{
			correctResponse = ResponseType.Error;
			Log("Both parts of the message are invalid, you must respond with an Error message.");
		}
		else if (correctParts[0] && correctParts[1])
		{
			correctResponse = ResponseType.Either;
			Log("Both parts of the message are valid, you may respond with either.");
		}
		else if (correctParts[0])
		{
			correctResponse = ResponseType.First;
			Log("Only the first part of the message is valid, you must respond with it.");
		}
		else if (correctParts[1])
		{
			correctResponse = ResponseType.Second;
			Log("Only the second part of the message is valid, you must respond with it.");
		}
		else
			Log("Something went wrong, no solution was identified.");
		DebugLog(CalculateSolution());
	}

	bool VerifySolution(bool check)
    {
		bool[] correct = new bool[4] { true, true, true, true };
		string input = Digits[4].text + Digits[5].text + Digits[6].text;
		if (check)
			Log("You sent Silo ID: " + Digits[0].text + Digits[1].text + Digits[2].text + " | Message: " + Digits[3].text + input + " | Location: " + Digits[7].text + Digits[8].text + Digits[9].text +  " | Authentication: " + ConfirmDigits[0].text + ConfirmDigits[1].text + ConfirmDigits[2].text + ConfirmDigits[3].text + ".");
		if (siloID != Digits[0].text + Digits[1].text + Digits[2].text)
        {
			correct[0] = false;
			if (check)
				Log("Silo ID is " + siloID + ", but you submitted " + Digits[0].text + Digits[1].text + Digits[2].text + ".");
        }
		string location = ToChar((Bomb.GetSolvableModuleNames().Count() % 36).ToString(), 0) + ToChar(((Bomb.GetSolvableModuleNames().Count() - Bomb.GetSolvedModuleNames().Count()) % 36).ToString(), 0) + ToChar((Bomb.GetSolvedModuleNames().Count() % 36).ToString(), 0);
		if (location != Digits[7].text + Digits[8].text + Digits[9].text)
        {
			correct[2] = false;
			if (check)
				Log("Location is " + location + ", but you submitted " + Digits[7].text + Digits[8].text + Digits[9].text + ".");
        }

		int[] usedCiphers = new int[2] { ToNum(outMessages[0][0].ToString(), 0) % 3, ToNum(outMessages[2][0].ToString(), 0) % 3 };
		if (usedCiphers.Contains(ToNum(Digits[3].text, 0) % 3))
        {
			correct[1] = false;
			if (check)
				Log("You received messages in " + Ciphers[ToNum(outMessages[0][0].ToString(), 0) % 3] + " cipher and " + Ciphers[ToNum(outMessages[2][0].ToString(), 0) % 3] + " cipher, but you submitted your message in " + Ciphers[ToNum(Digits[3].text, 0) % 3] + " cipher.");
		}

		string[] possible = new string[2] { Encryptor("" + outMessages[1][1] + outMessages[1][2] + outMessages[1][3], Digits[3].text, true), Encryptor("" + outMessages[3][1] + outMessages[3][2] + outMessages[3][3], Digits[3].text, true) };
		string answer = "";
		switch (correctResponse)
        {
			case ResponseType.Either:
				answer = possible[0];
				break;
			case ResponseType.First:
				answer = possible[0];
				break;
			case ResponseType.Second:
				answer = possible[1];
				break;
			case ResponseType.Jamming:
				answer = "" + possible[0][0] + possible[1][1] + possible[0][2];
				break;
			case ResponseType.Error:
				answer = "" + possible[1][0] + possible[0][1] + possible[1][2];
				break;
		}

		if (correctResponse == ResponseType.Either)
        {
			if (input != possible[0] && input != possible[1])
            {
				correct[1] = false;
				if (check)
					Log("With your selected cipher (" + Ciphers[ToNum(Digits[3].text, 0) % 3] + "), you could have sent " + possible[0] + " or " + possible[1] + ", but you sent " + input + ".");
			}
		}
		else
        {
			if (input != answer)
            {
				correct[1] = false;
				if (check)
					Log("With your selected cipher (" + Ciphers[ToNum(Digits[3].text, 0) % 3] + "), you needed to send " + (correctResponse == ResponseType.First ? "part one" : correctResponse == ResponseType.Second ? "part two" : correctResponse == ResponseType.Jamming ? "a jamming signal" : "an error signal") + ", encrypted as " + answer + ", but you sent " + input + ".");
			}
        }

		string decInput = Encryptor(input, Digits[3].text, false);
		int[] logAuth2 = new int[3] { 0, 0, 0 };
		if (correctColor == MessageColor.Green || correctColor == MessageColor.Yellow)
			for (int i = 0; i < 3; i++)
				logAuth2[i] = ToNum(decInput[i].ToString(), 0) * ToNum(siloID[i].ToString(), 0);
		else
			for (int i = 0; i < 3; i++)
				logAuth2[i] = ToNum(decInput[i].ToString(), 0) * ToNum(location[i].ToString(), 0);
		ansAuthCode = logAuth2[0] + logAuth2[1] + logAuth2[2];
		if (check)
			Log("With your type " + (correctColor == MessageColor.Green ? "Green-Alpha" : correctColor == MessageColor.Yellow ? "Yellow-Alpha" : "Red-Alpha") + " message and your decrypted message of " + decInput + ", your sums were " + logAuth2[0].ToString() + ", " + logAuth2[1].ToString() + " and " + logAuth2[2].ToString() + ", which totals up to " + ansAuthCode.ToString("0000") + ".");
		if (ansAuthCode.ToString("0000") != ConfirmDigits[0].text + ConfirmDigits[1].text + ConfirmDigits[2].text + ConfirmDigits[3].text)
        {
			correct[3] = false;
			if (check)
				Log("You submitted " + ConfirmDigits[0].text + ConfirmDigits[1].text + ConfirmDigits[2].text + ConfirmDigits[3].text + " as your authentication code, but the correct answer was " + ansAuthCode.ToString("0000") + ".");
        }
			
		if (check) 
			StartCoroutine(EndRoutine(correct));
		if (correct.Contains(false))
			return false;
		else
			return true;
	}

	IEnumerator EndRoutine(bool[] correct)
    {
		yield return new WaitForSeconds(1.0f);
		for (int i = 0; i < 14; i++)
        {
			if (i < 3)
            {
				if (correct[0])
					Digits[i].text = GoodLetters[i].ToString();
				else
					Digits[i].text = BadLetters[i].ToString();
            }
			else if (i < 7)
            {
				if (!correct[1] && tpAutosolve)
					Digits[i].text = "BYPA"[i - 3].ToString();
				else if (correct[1])
					Digits[i].text = GoodLetters[i].ToString();
				else
					Digits[i].text = BadLetters[i].ToString();
			}
			else if (i < 10)
			{
				if (!correct[2] && tpAutosolve)
					Digits[i].text = "BYP"[i - 7].ToString();
				else if (correct[2])
					Digits[i].text = GoodLetters[i].ToString();
				else
					Digits[i].text = BadLetters[i].ToString();
			}
			else
            {
				if (!correct[3] && tpAutosolve)
					ConfirmDigits[i - 10].text = "3333"[i - 10].ToString();
				else if (correct[3])
					ConfirmDigits[i - 10].text = GoodLetters[i].ToString();
				else
					ConfirmDigits[i - 10].text = BadLetters[i].ToString();
            }
			yield return new WaitForSeconds(0.5f);
		}
		if (!correct[0] || !correct[1] || !(correct[2] || tpAutosolve) || !(correct[3] || tpAutosolve))
        {
			Audio.PlaySoundAtTransform(ModuleSounds[2].name, transform);
			yield return new WaitForSeconds(4.5f);
			mStatus = Status.Start;
			Module.HandleStrike();
			CalculateConditions();
		}
		else
        {
			Audio.PlaySoundAtTransform(ModuleSounds[1].name, transform);
			yield return new WaitForSeconds(5.2f);
			mStatus = Status.Solved;
			Module.HandlePass();
        }
	}

	string CalculateSolution()
    {
		int[] usedCiphers = new int[2] { ToNum(outMessages[0][0].ToString(), 0) % 3, ToNum(outMessages[2][0].ToString(), 0) % 3 };
		int cipher = !usedCiphers.Contains(0) ? 0 : !usedCiphers.Contains(1) ? 1 : 2;
		string[] possible = new string[2] { Encryptor("" + outMessages[1][1] + outMessages[1][2] + outMessages[1][3], cipher.ToString(), true), Encryptor("" + outMessages[3][1] + outMessages[3][2] + outMessages[3][3], cipher.ToString(), true) };
		string answer = "";
		switch (correctResponse)
		{
			case ResponseType.Either:
				answer = possible[0];
				break;
			case ResponseType.First:
				answer = possible[0];
				break;
			case ResponseType.Second:
				answer = possible[1];
				break;
			case ResponseType.Jamming:
				answer = "" + possible[0][0] + possible[1][1] + possible[0][2];
				break;
			case ResponseType.Error:
				answer = "" + possible[1][0] + possible[0][1] + possible[1][2];
				break;
		}
		string message = cipher.ToString() + answer;
		string location= ToChar((Bomb.GetSolvableModuleNames().Count() % 36).ToString(), 0) + ToChar(((Bomb.GetSolvableModuleNames().Count() - Bomb.GetSolvedModuleNames().Count()) % 36).ToString(), 0) + ToChar((Bomb.GetSolvedModuleNames().Count() % 36).ToString(), 0);
		string decAnswer = Encryptor(answer, cipher.ToString(), false);
		int[] logAuth2 = new int[3] { 0, 0, 0 };
		if (correctColor == MessageColor.Green || correctColor == MessageColor.Yellow)
			for (int i = 0; i < 3; i++)
				logAuth2[i] = ToNum(decAnswer[i].ToString(), 0) * ToNum(siloID[i].ToString(), 0);
		else
			for (int i = 0; i < 3; i++)
				logAuth2[i] = ToNum(decAnswer[i].ToString(), 0) * ToNum(location[i].ToString(), 0);
		ansAuthCode = logAuth2[0] + logAuth2[1] + logAuth2[2];
		Log("With " + Bomb.GetSolvedModuleNames().Count().ToString() + " solved module(s), your solution could have been Silo: " + siloID.ToString() + " | Message: " + message + " | Location: " + location + " | Authentication Code: " + ansAuthCode.ToString("0000") + ".");
		return siloID.ToString() + message + location + ansAuthCode.ToString("0000");
	}

	string Encryptor (string message, string cipher, bool encrypt)
    {
		string output = "";
		int usedcipher = ToNum(cipher, 0) % 3;
		int offset = ToNum(siloID[0].ToString(), 0) > 0 ? ToNum(siloID[0].ToString(), 0) : ToNum(siloID[1].ToString(), 0) > 0 ? ToNum(siloID[1].ToString(), 0) : ToNum(siloID[2].ToString(), 0) > 0 ? ToNum(siloID[2].ToString(), 0) : 9;
		if (usedcipher == 0) // postmodern
		{
			for (int i = 0; i < message.Length; i++)
            {
				int targetPos = Array.IndexOf(PostModernAlphabet.ToCharArray(), message[i]) + (encrypt ? offset : offset * -1);
				while (targetPos < 0 || targetPos > 35)
				{
					if (targetPos < 0)
						targetPos += 36;
					if (targetPos > 35)
						targetPos -= 36;
				}
				output += PostModernAlphabet[targetPos];
			}
		}
		else if (usedcipher == 1) // rot18
		{
			for (int i = 0; i < message.Length; i++)
				output += ToChar(message[i].ToString(), 18);
		}
		else // modified atbash
        {
			for (int i = 0; i < message.Length; i++)
				output += ToChar((35 - ToNum(message[i].ToString(), 0)).ToString(), 0);
        }
		return output;
    }

	IEnumerator WaitingLightRoutine()
    {
		while (true)
        {
			yield return null;
			if (mStatus == Status.Start || mStatus == Status.Busy || mStatus == Status.Solved) waitingLight.enabled = false;
			else if (mStatus == Status.Waiting)
            {
				waitingLight.enabled = true;
				yield return new WaitForSeconds(0.5f);
				waitingLight.enabled = false;
				yield return new WaitForSeconds(5.0f);
			}
			else
            {
				waitingLight.enabled = true;
				yield return new WaitForSeconds(1.0f);
				waitingLight.enabled = false;
				yield return new WaitForSeconds(1.0f);
			}
		}
    }

	IEnumerator BusyLightRoutine()
    {
		while (true)
        {
			yield return null;
			busyLight.enabled = mStatus == Status.Busy;
		}
	}

	IEnumerator RotateLetters()
    {
		while (true)
        {
			for (int i = 0; i < 10; i++)
			{
				while (mStatus != Status.Input) yield return new WaitForSeconds(0.1f);
				if (activeDigits[i])
					Digits[i].text = ToChar(Rand.Range(0,36).ToString(), 0);
				yield return new WaitForSeconds(0.01f);
			}
			for (int i = 0; i < 4; i++)
            {
				while (mStatus != Status.Input) yield return new WaitForSeconds(0.1f);
				if (activeDigits[i + 10])
					ConfirmDigits[i].text = Rand.Range(0, 10).ToString();
				yield return new WaitForSeconds(0.01f);
			}
		}

    }

	IEnumerator AudioHandler(bool skip)
    {
		mStatus = Status.Waiting;
		if (!skip)
		{
			if (ZenModeActive)
            {
				DebugLog("Zen Mode detected, waiting 30 seconds.");
				yield return new WaitForSeconds(30.0f);
			}
			else
            {
				int startTime = (int)Bomb.GetTime();
				float timeFactor = Rand.Range(60, 81) * 0.01f * startTime;
				if (startTime < 300)
                {
					DebugLog("Not enough remaining time, waiting 30 seconds.");
					yield return new WaitForSeconds(30.0f);
				}
				else if (TimeModeActive)
                {
					DebugLog("Time Mode detected, waiting " + (startTime - timeFactor).ToString() + " seconds, or when the bomb time goes below " + timeFactor.ToString() + " seconds.");
					float timeElapsed = 0f;
					while (startTime - timeElapsed > timeFactor && timeFactor < Bomb.GetTime() && !tpAutosolve)
                    {
						timeElapsed += Time.deltaTime;
						yield return new WaitForSeconds(0.01f);
                    }
                }
				else
                {
					DebugLog("No special mode detected, waiting until bomb time " + timeFactor.ToString() + " seconds.");
					while (timeFactor < Bomb.GetTime() && !tpAutosolve) yield return new WaitForSeconds(0.01f);
				}
			}
		}
		else
		{
			DebugLog("Skip requested, 30 seconds until message.");
			yield return new WaitForSeconds(30.0f);
		}
        int VoiceIndex = Rand.Range(0, 3);
		mStatus = Status.Busy;
		Audio.PlaySoundAtTransform(ModuleSounds[3].name, transform);
		yield return new WaitForSeconds(2.0f);
		if (VoiceIndex == 0) //MouseTrap
        {
			DebugLog("Message given by MouseTrap.");
			Audio.PlaySoundAtTransform(MouseTrapStarts[correctColor == MessageColor.Green ? 2 : correctColor == MessageColor.Yellow ? 3 : 4].name, transform);
			yield return new WaitForSeconds(8.0f);
			Audio.PlaySoundAtTransform(MouseTrapStarts[1].name, transform);
			yield return new WaitForSeconds(2.4f);
			for (int i = 0; i < 4; i++)
            {
				Audio.PlaySoundAtTransform(MouseTrapSounds[ToNum(outMessages[0][i].ToString(), 0)].name, transform);
				yield return new WaitForSeconds(0.8f);
			}
			yield return new WaitForSeconds(0.4f);
			for (int i = 0; i < 4; i++)
			{
				Audio.PlaySoundAtTransform(MouseTrapSounds[ToNum(outMessages[2][i].ToString(), 0)].name, transform);
				yield return new WaitForSeconds(0.8f);
			}
			Audio.PlaySoundAtTransform(MouseTrapStarts[0].name, transform);
			yield return new WaitForSeconds(1.4f);
			for (int i = 0; i < 4; i++)
			{
				
				Audio.PlaySoundAtTransform(MouseTrapSounds[int.Parse(outAuthCode.ToString("0000")[i].ToString())].name, transform);
				yield return new WaitForSeconds(0.8f);
			}

		}
		else if (VoiceIndex == 1) //GreyGoose
        {
			DebugLog("Message given by GreyGoose.");
			Audio.PlaySoundAtTransform(GreyGooseStarts[correctColor == MessageColor.Green ? 2 : correctColor == MessageColor.Yellow ? 3 : 4].name, transform);
			yield return new WaitForSeconds(10.0f);
			Audio.PlaySoundAtTransform(GreyGooseStarts[1].name, transform);
			yield return new WaitForSeconds(2.4f);
			for (int i = 0; i < 4; i++)
			{
				Audio.PlaySoundAtTransform(GreyGooseSounds[ToNum(outMessages[0][i].ToString(), 0)].name, transform);
				yield return new WaitForSeconds(0.8f);
			}
			yield return new WaitForSeconds(0.4f);
			for (int i = 0; i < 4; i++)
			{
				Audio.PlaySoundAtTransform(GreyGooseSounds[ToNum(outMessages[2][i].ToString(), 0)].name, transform);
				yield return new WaitForSeconds(0.8f);
			}
			Audio.PlaySoundAtTransform(GreyGooseStarts[0].name, transform);
			yield return new WaitForSeconds(1.4f);
			for (int i = 0; i < 4; i++)
			{

				Audio.PlaySoundAtTransform(GreyGooseSounds[int.Parse(outAuthCode.ToString("0000")[i].ToString())].name, transform);
				yield return new WaitForSeconds(0.8f);
			}
		}
		else //BlackHole
        {
			DebugLog("Message given by BlackHole.");
			Audio.PlaySoundAtTransform(BlackHoleStarts[correctColor == MessageColor.Green ? 2 : correctColor == MessageColor.Yellow ? 3 : 4].name, transform);
			yield return new WaitForSeconds(10.0f);
			Audio.PlaySoundAtTransform(BlackHoleStarts[1].name, transform);
			yield return new WaitForSeconds(2.0f);
			for (int i = 0; i < 4; i++)
			{
				Audio.PlaySoundAtTransform(BlackHoleSounds[ToNum(outMessages[0][i].ToString(), 0)].name, transform);
				yield return new WaitForSeconds(0.9f);
			}
			yield return new WaitForSeconds(0.4f);
			for (int i = 0; i < 4; i++)
			{
				Audio.PlaySoundAtTransform(BlackHoleSounds[ToNum(outMessages[2][i].ToString(), 0)].name, transform);
				yield return new WaitForSeconds(0.9f);
			}
			Audio.PlaySoundAtTransform(BlackHoleStarts[0].name, transform);
			yield return new WaitForSeconds(1.5f);
			for (int i = 0; i < 4; i++)
			{

				Audio.PlaySoundAtTransform(BlackHoleSounds[int.Parse(outAuthCode.ToString("0000")[i].ToString())].name, transform);
				yield return new WaitForSeconds(0.9f);
			}
		}

		activeDigits = new bool[14] { true, true, true, true, true, true, true, true, true, true, true, true, true, true };
		mStatus = Status.Input;
    }

	public readonly string TwitchHelpMessage = "Receive the message with !{0} receive. Send the message with !{0} send. Change the displays with !{0} (display) (input). Valid displays are Silo, Message, Location, and Authentication/Auth.";

	IEnumerator ProcessTwitchCommand(string command)
    {
		string[] parameters = command.ToUpperInvariant().Trim().Split(' ').ToArray();
		if (parameters.Count() == 1)
        {
			if (parameters[0] == "RECEIVE")
			{
				yield return null;
				ReceiveButton.OnInteract();
			}
			else if (parameters[0] == "SEND")
			{
				yield return null;
				SendButton.OnInteract();
				if (mStatus == Status.Busy)
                {
					if (VerifySolution(false))
						yield return "solve";
					else
						yield return "strike";
				}
			}
        }
		else if (parameters.Count() == 2)
        {
			if (parameters[0] == "SILO")
            {
				if (mStatus != Status.Input)
                {
					yield return "sendtochaterror Sorry, you cannot input right now.";
					yield break;
                }
				if (parameters[1].Length == 3 && parameters[1].All(x => AlphabetandNumbers.Contains(x)))
                {
					yield return null;
					for (int i = 0; i < 3; i++)
                    {
						DigitArrows[2 * i].OnInteract();
						bool forward = Math.Abs(Array.IndexOf(AlphabetandNumbers.ToCharArray(), Digits[i].text[0]) - Array.IndexOf(AlphabetandNumbers.ToCharArray(), parameters[1][i])) < 18;
						bool reverse = Array.IndexOf(AlphabetandNumbers.ToCharArray(), Digits[i].text[0]) > Array.IndexOf(AlphabetandNumbers.ToCharArray(), parameters[1][i]);
						while (Digits[i].text[0] != parameters[1][i])
						{
							DigitArrows[2 * i + (forward ^ reverse ? 0 : 1)].OnInteract();
							yield return new WaitForSeconds(0.1f);
						}
                    }
                }
            }
			else if (parameters[0] == "MESSAGE")
            {
				if (mStatus != Status.Input)
				{
					yield return "sendtochaterror Sorry, you cannot input right now.";
					yield break;
				}
				if (parameters[1].Length == 4 && parameters[1].All(x => AlphabetandNumbers.Contains(x)))
				{
					yield return null;
					for (int i = 3; i < 7; i++)
					{
						DigitArrows[2 * i].OnInteract();
						bool forward = Math.Abs(Array.IndexOf(AlphabetandNumbers.ToCharArray(), Digits[i].text[0]) - Array.IndexOf(AlphabetandNumbers.ToCharArray(), parameters[1][i - 3])) < 18;
						bool reverse = Array.IndexOf(AlphabetandNumbers.ToCharArray(), Digits[i].text[0]) > Array.IndexOf(AlphabetandNumbers.ToCharArray(), parameters[1][i - 3]);
						while (Digits[i].text[0] != parameters[1][i - 3])
						{
							DigitArrows[2 * i + (forward ^ reverse ? 0 : 1)].OnInteract();
							yield return new WaitForSeconds(0.1f);
						}
					}
				}
			}
			else if (parameters[0] == "LOCATION")
            {
				if (mStatus != Status.Input)
				{
					yield return "sendtochaterror Sorry, you cannot input right now.";
					yield break;
				}
				if (parameters[1].Length == 3 && parameters[1].All(x => AlphabetandNumbers.Contains(x)))
				{
					yield return null;
					for (int i = 7; i < 10; i++)
					{
						DigitArrows[2 * i].OnInteract();
						bool forward = Math.Abs(Array.IndexOf(AlphabetandNumbers.ToCharArray(), Digits[i].text[0]) - Array.IndexOf(AlphabetandNumbers.ToCharArray(), parameters[1][i - 7])) < 18;
						bool reverse = Array.IndexOf(AlphabetandNumbers.ToCharArray(), Digits[i].text[0]) > Array.IndexOf(AlphabetandNumbers.ToCharArray(), parameters[1][i - 7]);
						while (Digits[i].text[0] != parameters[1][i - 7])
						{
							DigitArrows[2 * i + (forward ^ reverse ? 0 : 1)].OnInteract();
							yield return new WaitForSeconds(0.1f);
						}
					}
				}
			}
			else if (parameters[0] == "AUTH" || parameters[0] == "AUTHENTICATION")
            {
				if (mStatus != Status.Input)
				{
					yield return "sendtochaterror Sorry, you cannot input right now.";
					yield break;
				}
				if (parameters[1].Length < 5 && parameters[1].All(x => Numbers.Contains(x)))
				{
					yield return null;
					parameters[1] = parameters[1].PadLeft(4, '0');
					for (int i = 0; i < 4; i++)
					{
						ConfirmationArrows[2 * i].OnInteract();
						bool forward = Math.Abs(Array.IndexOf(Numbers.ToCharArray(), ConfirmDigits[i].text[0]) - Array.IndexOf(Numbers.ToCharArray(), parameters[1][i])) < 5;
						bool reverse = Array.IndexOf(Numbers.ToCharArray(), ConfirmDigits[i].text[0]) > Array.IndexOf(Numbers.ToCharArray(), parameters[1][i]);
						while (ConfirmDigits[i].text[0] != parameters[1][i])
						{
							ConfirmationArrows[2 * i + (forward ^ reverse ? 0 : 1)].OnInteract();
							yield return new WaitForSeconds(0.1f);
						}
					}
				}
			}
        }
		yield break;
    }

	IEnumerator TwitchHandleForcedSolve()
    {
		tpAutosolve = true;
		DebugLog("TP Autosolver in use.");
		while (mStatus != Status.Input)
		{
			if (mStatus == Status.Start)
				ReceiveButton.OnInteract();
			yield return true;
		}
		string answer = CalculateSolution();
		for (int i = 0; i < 10; i++)
        {
			DigitArrows[2 * i].OnInteract();
			bool forward = Math.Abs(Array.IndexOf(AlphabetandNumbers.ToCharArray(), Digits[i].text[0]) - Array.IndexOf(AlphabetandNumbers.ToCharArray(), answer[i])) < 18;
			bool reverse = Array.IndexOf(AlphabetandNumbers.ToCharArray(), Digits[i].text[0]) > Array.IndexOf(AlphabetandNumbers.ToCharArray(), answer[i]);
			while (Digits[i].text[0] != answer[i])
			{
				DigitArrows[2 * i + (forward ^ reverse ? 0 : 1)].OnInteract();
				yield return new WaitForSeconds(0.1f);
			}
		}
		for (int i = 0; i < 4; i++)
        {
			ConfirmationArrows[2 * i].OnInteract();
			bool forward = Math.Abs(Array.IndexOf(Numbers.ToCharArray(), ConfirmDigits[i].text[0]) - Array.IndexOf(Numbers.ToCharArray(), answer[i + 10])) < 5;
			bool reverse = Array.IndexOf(Numbers.ToCharArray(), ConfirmDigits[i].text[0]) > Array.IndexOf(Numbers.ToCharArray(), answer[i + 10]);
			while (ConfirmDigits[i].text[0] != answer[i + 10])
			{
				ConfirmationArrows[2 * i + (forward ^ reverse ? 0 : 1)].OnInteract();
				yield return new WaitForSeconds(0.1f);
			}
		}
		SendButton.OnInteract();
		yield break;
    }
}
