using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum MenstruationStatus
{
	None,
	PreOvulation,
	Ovulation,
	Insemination,
	Pregnant
}

[System.Serializable]
public abstract class Humanoid_Womb {

	public MenstruationStatus State;

	[SerializeField] private int refID;
	Character_Trainable ownerStorage = null;
	Character_Trainable owner
	{
		get
		{
			if (ownerStorage == null) ownerStorage = scr_System_CampaignManager.current.FindInstanceByID(refID);
			return ownerStorage;
		}
	}
    //float cum_capacity;

    int biological_state;
    protected int currentPower;

	public bool noAging = false;

	public Humanoid_Womb(int refID, bool noAging = false)
	{
		this.refID = refID;
		this.noAging = noAging;
		this.biological_state = 0;
		// WRITE THE FOLLOWING INTO CHILD CODE
		/*
		if (owner.getage() > 0){
			womb_quickstart();
		}*/

	}

	protected void womb_quickstart()
	{
		// assuming all values are initialized
		// this function should be called in child classes' inherited constructors;
		if (owner.Age > 0)
		{

			int cycleThreshold = (int)cycle_threshold;
			int totalLength = (int)(365 * owner.Age);
			for (int j = 0; j < totalLength; j += cycleThreshold)
			{
				if (biological_state == 0 && puberty_threshold < 0)
				{
					biological_state += 1;
				}
				else if (puberty_threshold >= 0)
				{
					puberty_threshold -= cycle_threshold;
				}
				ovulation();
			}

			cycle_value = Random.Range(0f, cycle_threshold);
		}
		else
		{
			// do nothing
		}
	}

	//====================shared data
	protected float puberty_threshold;   //each day add rand to this
	protected float puberty_variation;  //as percentile

	protected float cycle_threshold;        // represent average day for cycle, human 28
	protected float cycle_variation;
	protected float cycle_value;

	protected string menstruation_status;

	public string Menstruation_Status { get
        {
			return menstruation_status;
		} }


	public void dayTick()
	{
		if (biological_state == 0)
		{
			dayTick_Puberty();  //once we reach pub this is useless huh
		}
		else
		{
			if (isPregnant)
			{
				// TODO pregnancy tick
			}
			else
			{
				dayTick_Cycle();    //mens cycle
									// does cycle include preg check ?
			}
		}



	}

	bool isPregnant;



	public virtual void dayTick_Cycle()
	{

		cycle_value += getRandwithVariation(1.0f, cycle_variation);
		if (cycle_value > cycle_threshold)
		{
			cycle_value = 0f;
		}
		// then add specific override for each cycle stage
	}

	public void dayTick_Puberty()
	{
		// this will not be called once pub is reached
		puberty_threshold -= getRandwithVariation(1.0f, puberty_variation);
		if (puberty_threshold < 0)
		{
			biological_state += 1;  // then it will no longer be called;
		}

		// TODO  check sex and advance pub earlier
	}

	protected float getRandwithVariation(float average, float variationPercentile)
	{
		return Random.Range(average *(1.0f-variationPercentile), average *(1.0f + variationPercentile));
	}

	protected int getRandwithVariation(int average, int variationInt)
	{
		return Random.Range(average - variationInt, average + variationInt);
	}

	//====================shared data
	//???????????????
	protected int ovulation_power_average;   // 1.2m - 1.8m
	protected int ovulation_power_variation;
	protected int ovulation_quantity_average;     // 300 - 900
	protected int ovulation_quantity_variation;

	protected float fertility; // in percentile, how muuch of one ovulation should survive

	protected int pregnancy;
	public int Pregnancy { get { return pregnancy; } }
	public int ovulation()
	{
		int ovuPower = getRandwithVariation(ovulation_quantity_average, ovulation_quantity_variation);
		int ovum = 0;
		if (biological_state == 0)
		{
			currentPower -= (ovuPower * 10);
		}
		else
		{
			if (!noAging) currentPower -= ovuPower;
			float randFertility = getRandwithVariation(fertility, (float)(fertility * 0.01));
			while (randFertility > 1.0f)
			{
				ovum += 1;
				randFertility -= 1f;
			}
			if (randFertility > 0.0f && Random.Range(0f, 1f) < randFertility)
			{
				ovum += 1;
			}
		}
		return ovum;
	}

	//Pregnancy preg;
	//bool can_receive_cum = true;
	//bool can_incubate_egg = true;




	public string getPregnancy()
	{
		return "None";
	}


}
[System.Serializable]
public class Womb_Human : Humanoid_Womb
{

	// refer to womb for base data


	// mens cycle
	// bleeding - follicular - ovu - luteal 28days

	// start at age 12 ends at age 50

	public Womb_Human(int owner, bool noAging) : base(owner, noAging)
	{
		this.puberty_threshold = 4380;
		this.puberty_variation = 0.1f;

		this.cycle_threshold = 28f;
		this.cycle_variation = 0.1f;
		this.cycle_value = 0f;

		this.ovulation_power_average = 1500000;
		this.ovulation_power_variation = 300000;

		this.ovulation_quantity_average = 600;
		this.ovulation_quantity_variation = 300;

		this.fertility = 1.0f;

		this.currentPower = getRandwithVariation(ovulation_power_average, ovulation_power_variation);

		womb_quickstart();

	}

	int stage1 = 5;     //bleeding
	int stage2 = 14;    // folli
	int stage3 = 15;    // ovu
						//int stage4 = 28; = cycle_threshold

	public override void dayTick_Cycle()
	{
		base.dayTick_Cycle(); //setup var
							  // cycle_value
		if ((int)cycle_value <= stage1)
		{
			//kill all egg
			pregnancy = -1;
			menstruation_status = "mensual";
			State = MenstruationStatus.None;
		}
		else if ((int)cycle_value <= stage2)
		{
			//
			menstruation_status = "follicular";
			pregnancy = -1;
			State = MenstruationStatus.PreOvulation;
		}
		else if ((int)cycle_value <= stage3)
		{
			menstruation_status = "ovulation";
			if (pregnancy == -1)
			{
				pregnancy = ovulation();
				State = MenstruationStatus.Ovulation;
			}
		}
		else
		{
			menstruation_status = "luteal";
			State = MenstruationStatus.None;
		}

	}

}
[System.Serializable]
public class Womb_Elf : Humanoid_Womb
{

	// basically 10 time slower than human

	public Womb_Elf(int owner, bool noAging) : base(owner, noAging)
	{
		this.puberty_threshold = 43800; // 10 times longer to reach pub
		this.puberty_variation = 0.1f;

		this.cycle_threshold = 28f;     // same as human
		this.cycle_variation = 0.1f;
		this.cycle_value = 0f;

		this.ovulation_power_average = 1500000;     // same
		this.ovulation_power_variation = 300000;

		this.ovulation_quantity_average = 60;      // 10 times less than human
		this.ovulation_quantity_variation = 30;

		this.fertility = 0.2f;      // what to do with this

		this.currentPower = getRandwithVariation(ovulation_power_average, ovulation_power_variation);

		womb_quickstart();
	}


	// mens cycle
	// bleeding - follicular - ovu - luteal 28days
	int stage1 = 5;     //bleeding
	int stage2 = 14;    // folli
	int stage3 = 15;    // ovu
						//int stage4 = 28; = cycle_threshold
						// start at age 12 ends at age 50

	public override void dayTick_Cycle()
	{
		base.dayTick_Cycle();
		// cycle_value
		if ((int)cycle_value <= stage1)
		{
			//kill all egg
			pregnancy = -1;
			menstruation_status = "mensual";
			State = MenstruationStatus.None;
		}
		else if ((int)cycle_value <= stage2)
		{
			//
			menstruation_status = "follicular";
			pregnancy = -1;
			State = MenstruationStatus.PreOvulation;
		}
		else if ((int)cycle_value <= stage3)
		{
			menstruation_status = "ovulation";
			if (pregnancy == -1)
			{
				pregnancy = ovulation();
				State = MenstruationStatus.Ovulation;
			}
		}
		else
		{
			menstruation_status = "luteal";
			State = MenstruationStatus.None;
		}
	}
}

[System.Serializable]
public class Womb_Furry : Humanoid_Womb
{

	public Womb_Furry(int owner, bool noAging) : base(owner, noAging)
	{
		this.puberty_threshold = 2380;  // around half
		this.puberty_variation = 0.1f;

		this.cycle_threshold = 180f;     // 5 month heat
		this.cycle_variation = 0.2f;
		this.cycle_value = 0f;

		this.ovulation_power_average = 1500000;
		this.ovulation_power_variation = 300000;

		this.ovulation_quantity_average = 6000; // 10 times amount
		this.ovulation_quantity_variation = 3000;

		this.fertility = 4.0f;

		this.currentPower = getRandwithVariation(ovulation_power_average, ovulation_power_variation);

		womb_quickstart();

	}
	// model after dog
	// total life 10-13yrs
	// day sleep 12-14h
	// pregnancy 2month

	// 1ST HEAT AGE 6-24 MONTH
	// 2 HEAT PER YEAR average 5-11 month

	// proestrus (bleeding, 6-11 days) 
	// estrus (breeding, 5-9 or 1-20days)
	// diestrus (include estrus 2-3week + 1-2week + max1month)
	// anestrus (tissue repair, 4month)
	int stage1 = 8;
	int stage2 = 16;
	int stage3 = 60;

	public override void dayTick_Cycle()
	{
		base.dayTick_Cycle();
		// cycle_value
		if ((int)cycle_value <= stage1)
		{
			//kill all egg
			menstruation_status = "proestrus";
			pregnancy = -1;
			State = MenstruationStatus.PreOvulation;
		}
		else if ((int)cycle_value <= stage2)
		{
			//
			menstruation_status = "estrus";
			if (pregnancy == -1)
			{
				pregnancy = ovulation();
				State = MenstruationStatus.Ovulation;
			}
		}
		else if ((int)cycle_value <= stage3)
		{
			menstruation_status = "diestrus";
			State = MenstruationStatus.None;
		}
		else
		{
			pregnancy = -1;
			menstruation_status = "anestrus";
			State = MenstruationStatus.None;
		}
	}

}




